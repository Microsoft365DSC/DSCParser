using System;
using System.Collections.Generic;
using ImplementedAsType = Microsoft.PowerShell.DesiredStateConfiguration.ImplementedAsType;
using DscResourceInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourceInfo;
using DscResourcePropertyInfo = Microsoft.PowerShell.DesiredStateConfiguration.DscResourcePropertyInfo;

namespace DSCParser.CSharp
{
    internal sealed class DscResourceInfoMapper
    {
        public static DscResourceInfo MapPSObjectToResourceInfo(dynamic psObject)
        {
            if (psObject is null) throw new ArgumentNullException(nameof(psObject));

            DscResourceInfo resourceInfo = new()
            {
                ResourceType = psObject.ResourceType,
                CompanyName = psObject.CompanyName,
                FriendlyName = psObject.FriendlyName,
                Module = psObject.Module,
                Path = psObject.Path,
                ParentPath = psObject.ParentPath,
                ImplementedAs = Enum.Parse(typeof(ImplementedAsType), psObject.ImplementedAs.ToString()),
                Name = psObject.Name
            };

            List<DscResourcePropertyInfo> props = [];
            foreach (object obj in psObject.Properties)
            {
                props.Add(MapToDscResourcePropertyInfo(obj));
            }
            resourceInfo.UpdateProperties(props);

            return resourceInfo;
        }

        public static DscResourcePropertyInfo MapToDscResourcePropertyInfo(dynamic psObjectPropery)
        {
            DscResourcePropertyInfo propertyInfo = new()
            {
                Name = psObjectPropery.Name,
                PropertyType = psObjectPropery.PropertyType,
                IsMandatory = psObjectPropery.IsMandatory
            };

            List<string> newValues = [];
            foreach (string value in psObjectPropery.Values)
            {
                newValues.Add(value);
            }
            propertyInfo.Values = newValues;
            return propertyInfo;
        }
    }
}