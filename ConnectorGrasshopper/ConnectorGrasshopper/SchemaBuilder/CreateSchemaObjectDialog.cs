﻿using System;
using System.Collections.Generic;
using Eto.Forms;
using Eto.Drawing;
using System.Linq;
using System.Reflection;
using Speckle.Core.Kits;

namespace ConnectorGrasshopper
{
  public class CreateSchemaObjectDialog : Dialog
  {
    private TreeGridView tree;
    private ListBox list;
    private SearchBox search;
    private TextArea description;

    private List<Type> types;
    private List<Type> typesFiltered;

    private Dictionary<string, int> counts = new Dictionary<string, int>();

    public bool HasResult = false;

    public CSOViewModel model;

    public CreateSchemaObjectDialog()
    {

      model = new CSOViewModel();
      DataContext = model;

      Title = "Create an Object by Schema";
      Padding = 5;
      Resizable = true;

      types = ListAvailableTypes();
      typesFiltered = types;

      search = new SearchBox
      {
        PlaceholderText = "Search for a schema class"
      };
      search.Focus();
      search.TextChanged += Search_TextChanged;


      //list = new ListBox
      //{
      //  Size = new Size(200, 200),
      //  ItemTextBinding = Binding.Property<Type, string>(x => x.Name),
      //  DataStore = typesFiltered,
      //  SelectedIndex = 0
      //};
      //list.SelectedIndexBinding.BindDataContext((CSOViewModel m) => m.SelectedIndex, DualBindingMode.OneWayToSource);
      //list.SelectedValueBinding.BindDataContext((CSOViewModel m) => m.SelectedType, DualBindingMode.OneWayToSource);


      tree = new TreeGridView { Size = new Size(200, 200) };
      tree.Columns.Add(new GridColumn { DataCell = new TextBoxCell(0) });
      tree.DataStore = GenerateTree();
      tree.BindDataContext(x => x.SelectedItem, (CSOViewModel m) => m.SelectedItem, DualBindingMode.OneWayToSource);

      description = new TextArea
      {
        ReadOnly = true,
        Size = new Size(200, 200)
      };

      description.TextBinding.BindDataContext(Binding.Property((CSOViewModel m) => m.SelectedItem).
        Convert(x => GetDescription(x)), DualBindingMode.OneWay);

      Content = new TableLayout
      {
        Spacing = new Size(5, 5),
        Padding = new Padding(10),
        Rows =
        {
          new TableRow(search),
          new TableRow(tree, description),
        }
      };

      // buttons
      DefaultButton = new Button { Text = "Create" };
      DefaultButton.BindDataContext(x => x.Enabled, Binding.Property((CSOViewModel m) => m.SelectedItem)
        .Convert(x => x != null && x.Tag != null), DualBindingMode.OneWay);

      DefaultButton.Click += (sender, e) =>
      {
        HasResult = true;
        Close();
      };
      PositiveButtons.Add(DefaultButton);

      AbortButton = new Button { Text = "C&ancel" };
      AbortButton.Click += (sender, e) => Close();
      NegativeButtons.Add(AbortButton);

    }

    private TreeGridItemCollection GenerateTree()
    {
      //create a tree of dictionaries to ensure uniqueness of namespaces and define structure
      var tree = new Dictionary<string, object>();
      counts = new Dictionary<string, int>();
      foreach (var type in typesFiltered)
      {
        RecurseNamespace(type.Namespace.Split('.'), tree, type);
        IncreaseCounts(type.Namespace);
      }

      var item = new TreeGridItem();
      var collection = new TreeGridItemCollection();
      RecurseTree(tree, item);
      collection.AddRange(item.Children);

      return collection;
    }

    private void RecurseNamespace(string[] ns, Dictionary<string, object> tree, Type t)
    {
      var key = ns[0];
      if (!tree.ContainsKey(ns[0]))
        tree[key] = new Dictionary<string, object>();

      if (ns.Length > 1)
      {
        RecurseNamespace(ns.Skip(1).ToArray(), tree[key] as Dictionary<string, object>, t);
      }
      else
      {
        ((Dictionary<string, object>)tree[key])[t.Name] = t;
      }
    }

    private void RecurseTree(Dictionary<string, object> tree, TreeGridItem item, string ns = "")
    {
      foreach (var key in tree.Keys)
      {

        if (tree[key] is Type t)
        {
          var child = new TreeGridItem(t.Name);
          child.Tag = t;
          item.Children.Add(child);
        }
        else if (tree[key] is Dictionary<string, object> d)
        {
          var subNs = (ns == "") ? key : $"{ns}.{key}"; //fetch count from dictionary
          var count = counts.ContainsKey(subNs) ? counts[subNs] : 0;
          var child = new TreeGridItem($"{key} ({count})");
          child.Expanded = !string.IsNullOrEmpty(search.Text);
          RecurseTree(d, child, subNs);
          item.Children.Add(child);

        }
      }
    }

    private void IncreaseCounts(string ns)
    {
      var parts = ns.Split('.');
      for (var i = 0; i < parts.Length; i++)
      {
        var name = string.Join(".", parts.Take(i + 1));
        if (!counts.ContainsKey(name))
          counts[name] = 1;
        else
          counts[name]++;
      }
    }


    private List<Type> ListAvailableTypes()
    {
      return KitManager.Types.Where(x => x.GetCustomAttribute<SchemaIgnoreAttribute>() == null).ToList();
    }

    //TODO: expand items?
    //TODO: add debounce? optimize loops?
    private void Search_TextChanged(object sender, EventArgs e)
    {

      if (!string.IsNullOrEmpty(search.Text))
        typesFiltered = types.Where(x => x.Name.ToLowerInvariant().Contains(search.Text.ToLowerInvariant())).ToList();
      else
        typesFiltered = types;

      tree.DataStore = GenerateTree();
      //list.DataStore = typesFiltered;
    }

    private string GetDescription(TreeGridItem t)
    {
      if (t == null || (Type)t.Tag == null)
        return "";
      var type = (Type)t.Tag;

      var description = "";

      var attr = type.GetCustomAttribute<SchemaDescriptionAttribute>();
      if (attr != null)
      {
        description += attr.Description + "\n\n";
      }

      var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.GetCustomAttribute<SchemaIgnoreAttribute>() == null && x.Name != "Item");

      //put optional props at the bottom
      var optionalProps = props.Where(x => x.GetCustomAttribute<SchemaOptionalAttribute>() != null).OrderBy(x => x.PropertyType.ToString()).ThenBy(x => x.Name);
      var nonOptionalProps = props.Where(x => x.GetCustomAttribute<SchemaOptionalAttribute>() == null).OrderBy(x => x.PropertyType.ToString()).ThenBy(x => x.Name);
      props = nonOptionalProps;
      props= props.Concat(optionalProps);

      if (props.Any())
      {
        description += "Inputs:\n";
        foreach (var p in props)
        {
          var inputDesc = p.GetCustomAttribute<SchemaDescriptionAttribute>();
          var d = inputDesc != null ? $": {inputDesc.Description}" : "";
          description += $"\n- {p.Name} ({p.PropertyType.Name}){d}";
        }

      }



      return description;
    }

    //rtf description, not working
    //private string GetDescription(TreeGridItem t)
    //{
    //  if (t == null || (Type)t.Tag == null)
    //    return "";
    //  var type = (Type)t.Tag;

    //  var description = @"{\rtf1";

    //  var attr = type.GetCustomAttribute<SchemaDescriptionAttribute>();
    //  if (attr != null)
    //  {
    //    description += $@"\b Description: \b0 {attr.Description}\par";
    //  }

    //  var props = type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(x => x.GetCustomAttribute(typeof(SchemaIgnoreAttribute)) == null && x.Name != "Item");
    //  if (props.Any())
    //  {
    //    description += @"\b Inputs:\b0\par";
    //    foreach (var p in props)
    //      description += $@"\bullet  {p.Name} \i ({p.PropertyType.Name})\i0\par";
    //  }

    //  description += "}";

    //  return description;
    //}


  }

}