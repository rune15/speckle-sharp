using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Core.Models;
#if CIVIL
using Autodesk.Aec.ApplicationServices;
#endif

namespace Objects.Converter.AutocadCivil;

public static class Utils
{
  public static BlockTableRecord GetModelSpace(this Database db)
  {
    return (BlockTableRecord)SymbolUtilityServices.GetBlockModelSpaceId(db).GetObject(OpenMode.ForWrite);
  }

  public static ObjectId Append(this BlockTableRecord owner, Entity entity)
  {
    if (!entity.IsNewObject)
    {
      return entity.Id;
    }

    var tr = owner.Database.TransactionManager.TopTransaction;
    var id = owner.AppendEntity(entity);
    tr.AddNewlyCreatedDBObject(entity, true);
    return id;
  }

  public static Base GetObjectExtensionDictionaryAsBase(this DBObject source)
  {
    if (source is null || source.ExtensionDictionary == ObjectId.Null)
    {
      return null;
    }

    var extensionDictionaryBase = new Base();
    var tr = source.Database.TransactionManager.TopTransaction;
    var extensionDictionary = tr.GetObject(source.ExtensionDictionary, OpenMode.ForRead, false) as DBDictionary;
    foreach (var entry in extensionDictionary)
    {
      if (tr.GetObject(entry.Value, OpenMode.ForRead) is Xrecord xRecord) // sometimes these can be RXClass objects, in property sets
      {
        var entryBase = new Base();
        foreach (var xEntry in xRecord.Data)
        {
          entryBase[xEntry.TypeCode.ToString()] = xEntry.Value;
        }

        extensionDictionaryBase[$"{entry.Key}"] = entryBase;
      }
    }

    return extensionDictionaryBase;
  }
}

public partial class ConverterAutocadCivil
{
  private const string INVALID_CHARS = @"<>/\:;""?*|=,‘";

  private Dictionary<string, ObjectId> _lineTypeDictionary = new();
  public Dictionary<string, ObjectId> LineTypeDictionary
  {
    get
    {
      if (_lineTypeDictionary.Values.Count == 0)
      {
        var lineTypeTable = (LinetypeTable)Trans.GetObject(Doc.Database.LinetypeTableId, OpenMode.ForRead);
        foreach (ObjectId lineTypeId in lineTypeTable)
        {
          var linetype = (LinetypeTableRecord)Trans.GetObject(lineTypeId, OpenMode.ForRead);
          _lineTypeDictionary.Add(linetype.Name, lineTypeId);
        }
      }
      return _lineTypeDictionary;
    }
  }

  /// <summary>
  /// Removes invalid characters for Autocad layer and block names
  /// </summary>
  /// <param name="str"></param>
  /// <returns></returns>
  public static string RemoveInvalidChars(string str)
  {
    // using this to handle rhino nested layer syntax
    // replace "::" layer delimiter with "$" (acad standard)
    string cleanDelimiter = str.Replace("::", "$");

    // remove all other invalid chars
    return Regex.Replace(cleanDelimiter, $"[{INVALID_CHARS}]", string.Empty);
  }

  public static void AddNameAndDescriptionProperty(string name, string description, Base @base)
  {
    if (!string.IsNullOrEmpty(name))
    {
      @base["name"] = name;
    }

    if (!string.IsNullOrEmpty(description))
    {
      @base["description"] = description;
    }
  }

  /// <summary>
  /// Retrieves the handle from an input string
  /// </summary>
  /// <param name="str"></param>
  /// <returns></returns>
  public static bool GetHandle(string str, out Handle handle)
  {
    handle = new Handle();
    if (string.IsNullOrEmpty(str))
    {
      return false;
    }

    long l;
    try
    {
      l = Convert.ToInt64(str, 16);
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
    {
      return false;
    }

    handle = new Handle(l);

    return true;
  }

  /// <summary>
  /// Returns, if found, the corresponding doc element.
  /// The doc object can be null if the user deleted it.
  /// </summary>
  /// <param name="applicationId">Id of the application that originally created the element, in autocadcivil it's the handle</param>
  /// <returns>The element, if found, otherwise null</returns>
  public List<ObjectId> GetExistingElementsByApplicationId(string applicationId)
  {
    var ids = new List<ObjectId>();

    if (applicationId == null || ReceiveMode == ReceiveMode.Create)
    {
      return ids;
    }

    // first see if this appid is a handle (autocad appid)
    if (GetHandle(applicationId, out Handle handle))
    {
      if (Doc.Database.TryGetObjectId(handle, out ObjectId id))
      {
        return new List<ObjectId>() { id };
      }
    }

    // Create a TypedValue array to define the filter criteria
    TypedValue[] acTypValAr = new TypedValue[1];
    acTypValAr.SetValue(new TypedValue((int)DxfCode.ExtendedDataRegAppName, ApplicationIdKey), 0);

    // Create a selection filter for the applicationID xdata entry and find all objs with this field
    SelectionFilter acSelFtr = new(acTypValAr);
    var res = Doc.Editor.SelectAll(acSelFtr);
    if (res.Status == PromptStatus.None || res.Status == PromptStatus.Error)
    {
      return ids;
    }

    // loop through all obj with an appId
    foreach (var appIdObj in res.Value.GetObjectIds())
    {
      if (appIdObj.IsErased)
      {
        continue;
      }

      // get the db object from id
      var obj = Trans.GetObject(appIdObj, OpenMode.ForRead);
      if (obj != null)
      {
        foreach (var entry in obj.XData)
        {
          if (entry.Value as string == applicationId)
          {
            ids.Add(appIdObj);
            break;
          }
        }
      }
    }

    return ids;
  }

  public ObjectId GetFromObjectIdCollection(string name, ObjectIdCollection collection, bool useFirstIfNull = false)
  {
    var id = ObjectId.Null;
    if (string.IsNullOrEmpty(name) && !useFirstIfNull || string.IsNullOrEmpty(name) && collection.Count == 0)
    {
      return id;
    }

    foreach (ObjectId collectionId in collection)
    {
      var entity = Trans.GetObject(collectionId, OpenMode.ForRead);
      if (entity != null)
      {
        var props = entity.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
        if (props != null && props.CanRead)
        {
          var entityName = props.GetValue(entity) as string;
          if (entityName == name)
          {
            id = collectionId;
            break;
          }
        }
      }
    }

    if (id == ObjectId.Null && useFirstIfNull && collection.Count > 0)
    {
      id = collection[0];
    }

    return id;
  }

  public LayerTableRecord GetLayer(string path, OpenMode mode = OpenMode.ForRead)
  {
    if (!string.IsNullOrEmpty(path))
    {
      var layerTable = (LayerTable)Trans.GetObject(Doc.Database.LayerTableId, OpenMode.ForRead);
      if (layerTable.Has(path))
      {
        return (LayerTableRecord)Trans.GetObject(layerTable[path], mode);
      }
    }

    return null;
  }

  public bool MakeLayer(string name, out LayerTableRecord layer)
  {
    layer = null;

    if (!string.IsNullOrEmpty(name))
    {
      var layerTable = (LayerTable)Trans.GetObject(Doc.Database.LayerTableId, OpenMode.ForWrite);

      LayerTableRecord newLayer = new() { Name = name };
      try
      {
        layerTable.Add(newLayer);
        Trans.AddNewlyCreatedDBObject(newLayer, true);
        layer = newLayer;
        return true;
      }
      catch (Exception e) when (!e.IsFatal())
      {
        // Couldn't create a layer, but can use default layer instead.
        SpeckleLog.Logger.Error(e, $"Could not add new layer {name} to the layer table");
      }
    }

    return false;
  }

  #region Reference Point

  // CAUTION: these strings need to have the same values as in the connector bindings
  const string InternalOrigin = "Internal Origin (default)";
  const string UCS = "Current User Coordinate System";
  private Matrix3d _transform;
  private Matrix3d ReferencePointTransform
  {
    get
    {
      if (_transform == null || _transform == new Matrix3d())
      {
        // get from settings
        var referencePointSetting = Settings.TryGetValue("reference-point", out string value) ? value : string.Empty;
        _transform = GetReferencePointTransform(referencePointSetting);
      }
      return _transform;
    }
  }

  private Matrix3d GetReferencePointTransform(string type)
  {
    var referencePointTransform = Matrix3d.Identity;

    switch (type)
    {
      case InternalOrigin:
        break;
      case UCS:
        var cs = Doc.Editor.CurrentUserCoordinateSystem.CoordinateSystem3d;
        if (cs != null)
        {
          referencePointTransform = Matrix3d.AlignCoordinateSystem(
            Point3d.Origin,
            Vector3d.XAxis,
            Vector3d.YAxis,
            Vector3d.ZAxis,
            cs.Origin,
            cs.Xaxis,
            cs.Yaxis,
            cs.Zaxis
          );
        }

        break;
      default: // try to see if this is a named UCS
        using (Transaction tr = Doc.Database.TransactionManager.StartTransaction())
        {
          var UCSTable = tr.GetObject(Doc.Database.UcsTableId, OpenMode.ForRead) as UcsTable;
          if (UCSTable.Has(type))
          {
            var ucsRecord = tr.GetObject(UCSTable[type], OpenMode.ForRead) as UcsTableRecord;
            referencePointTransform = Matrix3d.AlignCoordinateSystem(
              Point3d.Origin,
              Vector3d.XAxis,
              Vector3d.YAxis,
              Vector3d.ZAxis,
              ucsRecord.Origin,
              ucsRecord.XAxis,
              ucsRecord.YAxis,
              ucsRecord.XAxis.CrossProduct(ucsRecord.YAxis)
            );
          }
          tr.Commit();
        }
        break;
    }

    return referencePointTransform;
  }

  /// <summary>
  /// For sending out of AutocadCivil, transforms a point relative to the reference point
  /// </summary>
  /// <param name="p"></param>
  /// <returns></returns>
  public Point3d ToExternalCoordinates(Point3d p)
  {
    return p.TransformBy(ReferencePointTransform.Inverse());
  }

  /// <summary>
  /// For sending out of AutocadCivil, transforms a vector relative to the reference point
  /// </summary>
  /// <param name="p"></param>
  /// <returns></returns>
  public Vector3d ToExternalCoordinates(Vector3d v)
  {
    return v.TransformBy(ReferencePointTransform.Inverse());
  }

  /// <summary>
  /// For receiving in to AutocadCivil, transforms a point relative to the reference point
  /// </summary>
  /// <param name="p"></param>
  /// <returns></returns>
  public Point3d ToInternalCoordinates(Point3d p)
  {
    return p.TransformBy(ReferencePointTransform);
  }

  /// <summary>
  /// For receiving in to AutocadCivil, transforms a vector relative to the reference point
  /// </summary>
  /// <param name="p"></param>
  /// <returns></returns>
  public Vector3d ToInternalCoordinates(Vector3d v)
  {
    return v.TransformBy(ReferencePointTransform);
  }
  #endregion

  #region app props
  public static string AutocadPropName = "AutocadProps";
  public static string CivilPropName = "CivilProps";
  #endregion

  #region units

  private string _modelUnits;
  public string ModelUnits
  {
    get
    {
      if (string.IsNullOrEmpty(_modelUnits))
      {
        _modelUnits = UnitToSpeckle(Doc.Database.Insunits);

#if CIVIL
        if (_modelUnits == Units.None)
        {
          // try to get the drawing unit instead
          using (Transaction tr = Doc.Database.TransactionManager.StartTransaction())
          {
            var id = DrawingSetupVariables.GetInstance(Doc.Database, false);
            var setupVariables = (DrawingSetupVariables)tr.GetObject(id, OpenMode.ForRead);
            var linearUnit = setupVariables.LinearUnit;
            _modelUnits = Units.GetUnitsFromString(linearUnit.ToString());
            tr.Commit();
          }
        }
#endif
      }
      return _modelUnits;
    }
  }

  private void SetUnits(Base geom)
  {
    geom["units"] = ModelUnits;
  }

  private double ScaleToNative(double value, string units)
  {
    var f = Units.GetConversionFactor(units, ModelUnits);
    return value * f;
  }

  // Note: Difference between International Foot and US Foot is ~ 0.0000006 as described in: https://www.pobonline.com/articles/98788-us-survey-feet-versus-international-feet
  private string UnitToSpeckle(UnitsValue units)
  {
    switch (units)
    {
      case UnitsValue.Millimeters:
        return Units.Millimeters;
      case UnitsValue.Centimeters:
        return Units.Centimeters;
      case UnitsValue.Meters:
        return Units.Meters;
      case UnitsValue.Kilometers:
        return Units.Kilometers;
      case UnitsValue.Inches:
      case UnitsValue.USSurveyInch:
        return Units.Inches;
      case UnitsValue.Feet:
      case UnitsValue.USSurveyFeet:
        return Units.Feet;
      case UnitsValue.Yards:
      case UnitsValue.USSurveyYard:
        return Units.Yards;
      case UnitsValue.Miles:
      case UnitsValue.USSurveyMile:
        return Units.Miles;
      case UnitsValue.Undefined:
        return Units.None;
      default:
        throw new SpeckleException($"The Unit System \"{units}\" is unsupported.");
    }
  }

  #endregion
}
