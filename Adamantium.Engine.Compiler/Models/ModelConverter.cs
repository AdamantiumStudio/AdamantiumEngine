using System;
using System.Collections.Generic;
using System.Threading;
using Adamantium.Core;
using Adamantium.Engine.Compiler.Converter;
using Adamantium.Engine.Compiler.Converter.Configs;
using Adamantium.Graphics.Core.Models;
using Adamantium.MVVM;

namespace Adamantium.Engine.Compiler.Models
{
    [ViewModel]
    public partial class ModelConverter
    {
        #region Variables

        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

        [Bindable] private Boolean convertationInProgress;
        [Bindable] private Boolean convertationFinished = true;

        #endregion


        /// <summary>What the imported file had that we could not understand. Empty until an import has run.</summary>
        public IReadOnlyList<String> UnsupportedFeatures { get; private set; } = Array.Empty<String>();

        //The parse is synchronous; the former name ImportFileAsync promised otherwise
        public SceneData ImportFile(string path, CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, cancellationToken);
            var converter = ModelConverterFactory.GetConverter(path, new ConversionConfig(true));

            ConvertationInProgress = true;
            ConvertationFinished = false;
            try
            {
                return converter.StartConversion(linked.Token);
            }
            catch (OperationCanceledException)
            {
                IsConvertationCanceled = true;
                return null;
            }
            finally
            {
                // In a finally on purpose: when the import threw is exactly when this list matters most
                UnsupportedFeatures = converter.UnsupportedFeatures;
                ConvertationInProgress = false;
                ConvertationFinished = true;
            }
        }

        #region Cancellation

        public Boolean IsConvertationCanceled { get; private set; }

        /// <summary>Asks a running import to stop. Cancellation is checked between libraries and on every geometry,
        /// so it does not take effect instantly - but it does take effect: this method used to only clear flags
        /// while the parse ran happily to the end.</summary>
        public void CancelConvertation()
        {
            cancellation.Cancel();
            IsConvertationCanceled = true;
        }

        #endregion

    }
}
