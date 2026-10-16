using System;
using Adamantium.MVVM;

namespace Adamantium.Engine.Compiler.Converter.Configs
{
   [ViewModel]
   public partial class ConversionConfig
   {

      public ConversionConfig(Boolean enableAll)
      {
         if (enableAll)
         {
            EnableAll();
         }
         else
         {
            SetDefaults();
         }

         // Any setting changed from here on marks the configuration changed.
         PropertyChanged += (_, e) =>
         {
            if (e.PropertyName != nameof(ConfigChanged)) ConfigChanged = true;
         };
      }

      #region Fields

      [Bindable] private bool individualSettingsEnabled;
      [Bindable] private bool geometryEnabled;
      [Bindable] private bool animationEnabled;
      [Bindable] private bool controllersEnabled;
      [Bindable] private bool lightsEnabled;
      [Bindable] private bool materialsEnabled;
      [Bindable] private bool imagesEnabled;
      [Bindable] private bool camerasEnabled;
      [Bindable] private bool matrixTransposeNeeded;
      [Bindable] private bool optimizeMeshes;
      [Bindable] private bool convertToLHDirectX;
      [Bindable] private bool convertToRHDirectX;
      [Bindable] private bool convertToOGL;
      private bool convertToVulkan;
      [Bindable] private bool configChanged;
      [Bindable] private bool calculateNormalsIfNotPresent;
      [Bindable] private bool calculateTangentsBitangentsIfNotPresent;
      [Bindable] private bool getMeshMatrices;
      [Bindable] private bool getLightsMatrices;
      [Bindable] private bool getCamerasMatrices;
      [Bindable] private bool getJoints;

      [Bindable] private bool isClockWise;
      private static string fileName = "ConvertationConfig.aecc"; //adamantium engine covertation config

      #endregion //Fields

      #region Properties

      public static String FileName
      {
         get { return fileName; }
      }

      public bool ConvertToVulkan
      {
         get => convertToVulkan;
         set => convertToVulkan = value;
      }

      #endregion //Properties

      public void SetDefaults()
      {
         individualSettingsEnabled = true;
         geometryEnabled = false;
         animationEnabled = false;
         controllersEnabled = false;
         lightsEnabled = false;
         imagesEnabled = false;
         materialsEnabled = false;
         matrixTransposeNeeded = true;
         convertToLHDirectX = true;
         convertToRHDirectX = false;
         convertToOGL = false;
         isClockWise = true;
         optimizeMeshes = true;
         getJoints = true;
      }

      public void EnableAll()
      {
         individualSettingsEnabled = true;
         geometryEnabled = true;
         animationEnabled = true;
         controllersEnabled = true;
         lightsEnabled = true;
         imagesEnabled = true;
         materialsEnabled = true;
         matrixTransposeNeeded = true;
         convertToLHDirectX = true;
         convertToRHDirectX = false;
         convertToOGL = false;
         isClockWise = true;
         optimizeMeshes = true;
         getMeshMatrices = true;
         getLightsMatrices = true;
         getCamerasMatrices = true;
         getJoints = true;
         calculateNormalsIfNotPresent = true;
         calculateTangentsBitangentsIfNotPresent = true;
      }
   }
}
