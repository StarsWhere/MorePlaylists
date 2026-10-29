using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using BeatSaberMarkupLanguage.Parser;
using HMUI;
using IPA.Utilities;
using MorePlaylists.Sources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MorePlaylists.Utilities;
using UnityEngine;
using Zenject;

namespace MorePlaylists.UI
{
    internal class SourceModalController
    {
        private readonly List<ISource> sources; 
        private readonly BSMLParser bsmlParser;
        private bool parsed;
        public ISource SelectedSource { get; private set; }
        public event Action<ISource>? DidSelectSource;
        
        [UIComponent("list")]
        private readonly CustomListTableData customListTableData = null!;

        [UIComponent("source-modal")]
        private ModalView sourceModalView = null!;

        [UIComponent("source-modal")]
        private readonly RectTransform sourceModalTransform = null!;

        private Vector3 sourceModalPosition;

        [UIParams]
        private readonly BSMLParserParams parserParams = null!;
        
        public SourceModalController(List<ISource> sources, BSMLParser bsmlParser)
        {
            this.sources = sources;
            this.bsmlParser = bsmlParser;
            SelectedSource = sources.First();
        }

        private void Parse(Transform parent)
        {
            if (!parsed)
            {
                bsmlParser.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "MorePlaylists.UI.Views.SourceModal.bsml"), parent.gameObject, this);
                sourceModalPosition = sourceModalTransform.position;
                parsed = true;
            }
            sourceModalTransform.SetParent(parent);
            sourceModalTransform.localPosition = sourceModalPosition;
            Accessors.AnimateCanvasAccessor(ref sourceModalView) = true;
        }

        [UIAction("#post-parse")]
        private void PostParse()
        {
            customListTableData.Data.Clear();
            foreach (var source in sources)
            {
                customListTableData.Data.Add(new CustomListTableData.CustomCellInfo(source.GetType().Name, "", source.Logo));
            }
            customListTableData.TableView.ReloadData();
            customListTableData.TableView.SelectCellWithIdx(0);
        }

        internal void ShowModal(Transform parent)
        {
            Parse(parent);
            parserParams.EmitEvent("close-source-modal");
            parserParams.EmitEvent("open-source-modal");
        }

        [UIAction("list-select")]
        private void Select(TableView tableView, int row)
        {
            SelectedSource = sources[row];
            DidSelectSource?.Invoke(SelectedSource);
            parserParams.EmitEvent("close-source-modal");
        }
    }
}
