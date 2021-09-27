﻿using System;
using System.Collections.Generic;
using System.Text;
using ETABSv1;
using Objects.Structural.Properties;
using Objects.Structural.Materials;

namespace Objects.Converter.ETABS
{
    public partial class ConverterETABS
    {
        public  Property1D Property1DToSpeckle(string name)
        {
            var speckleStructProperty1D = new Property1D();
            speckleStructProperty1D.name = name;
            string materialProp = null;
            Doc.Document.PropFrame.GetMaterial(name, ref materialProp);
            Material material = MaterialToSpeckle(materialProp);
            speckleStructProperty1D.material = material;
            return speckleStructProperty1D;
        }
    }
}