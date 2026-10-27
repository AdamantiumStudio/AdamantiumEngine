using System.Collections.Generic;
using System.Linq;

namespace Adamantium.Core.Fonts
{
    public class FeatureInfo
    {
        public string Tag { get; }
        public string FriendlyName { get; }
        public string Description { get; }
        
        public FeatureRegistration RegisteredBy { get; }
        
        public string[] EnableFeatures { get; private set; }
        
        public string[] DisableFeatures { get; private set; }

        public bool OverridesAllOtherFeatures { get; internal set; }
        
        public string[] AllowedFeatures { get; internal set; }
        
        private FeatureInfo(
            string tag, 
            string friendlyName, 
            FeatureRegistration registration, 
            string description)
        {
            Tag = tag;
            FriendlyName = friendlyName;
            Description = description;
            RegisteredBy = registration;
        }

        public static FeatureInfo Create(
            string tag, 
            string friendlyName, 
            FeatureRegistration registration,
            string description,
            IEnumerable<string> enableFeatures = null,
            IEnumerable<string> disableFeatures = null,
            bool overridesAllOtherFeatures = false,
            IEnumerable<string> allowedFeatures = null)
        {
            var feature = new FeatureInfo(tag, friendlyName, registration, description)
            {
                EnableFeatures = enableFeatures?.ToArray(),
                DisableFeatures = disableFeatures?.ToArray(),
                OverridesAllOtherFeatures = overridesAllOtherFeatures,
                AllowedFeatures = allowedFeatures?.ToArray()
            };
            return feature;
        }

        public override string ToString()
        {
            return $"Tag: {Tag}, Friendly name: {FriendlyName}";
        }
    }
}