using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// The dock's CODE tab (Part 12e), beside WAVES and TRANSCRIPT: the SystemVerilog the document prints as, read-only and
    /// printed again as it is drawn -- a drawn module as the text the run was made from, a written one as its own. One chip
    /// a file in the order a simulator reads them, the cells first and the testbench last; the file shown follows the module
    /// the circuit's window shows until another chip is chosen.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        /// <summary>The tab's name, and a file's chip's: this, and what the chip says.</summary>
        public const string CodeTabName = "Workbench code tab", CodeFileName = "Workbench code file ";

        private const float CodeTabLeft = TranscriptTabLeft + TranscriptTabWidth + 4f, CodeTabWidth = 80f, ChipGap = 8f;

        private ShellButton _codeTabButton;
        private VisualElement _codeTools;
        private CodeListingView _listingView;
        private CodeListing _shownListing;
        private int _listingFile = -1;
        private string _listingModule;

        /// <summary>Whether the dock shows the code.</summary>
        public bool CodeShown => _dockTab == DockTab.Code;

        /// <summary>The CODE tab's file, as it is shown.</summary>
        public CodeListingView ListingView => _listingView;

        /// <summary>What the chip of the file shown says: CELLS, TESTBENCH or a module's name; null with none.</summary>
        public string ListingCaption => _shownListing != null && _listingFile >= 0 ? _shownListing.Caption(_listingFile) : null;

        /// <summary>Shows the code, as its tab does.</summary>
        public void ShowCode() => ShowDockTab(DockTab.Code);

        /// <summary>The CODE tab beside TRANSCRIPT, and the row its chips go in.</summary>
        private void BuildCodeTab(VisualElement header, SchematicPalette palette)
        {
            _codeTabButton = Tab(CodeTabName, "CODE", palette, () => ShowDockTab(DockTab.Code));
            ShellTheme.Place(_codeTabButton, CodeTabLeft, 6f, CodeTabWidth, TabHeight);
            header.Add(_codeTabButton);

            _codeTools = new VisualElement { name = "code tools", pickingMode = PickingMode.Ignore };
            ShellTheme.Place(_codeTools, 0f, 0f, -1f, ShellLayout.DockHeaderHeight);
            _codeTools.style.right = 0f;
            header.Add(_codeTools);
        }

        private void BuildListingView(SchematicPalette palette)
        {
            _listingView = CodeListingView.Build(Root, palette);
            PlaceListing();
            _listingView.SetShown(_dockTab == DockTab.Code);
        }

        /// <summary>The listing over the dock under its header: where the waves and their legend are.</summary>
        private void PlaceListing()
        {
            if (_listingView == null)
                return;
            Rect2 waves = _regions.Waves, legend = _regions.Legend;
            _listingView.Place(new Vector2(waves.x, waves.y), new Vector2(waves.width, Mathf.Max(0f, legend.y + legend.height - waves.y)));
        }

        /// <summary>
        /// Each frame the CODE tab is shown: the listing printed again once the document has changed, and the file shown
        /// following the module the circuit's window shows -- a file chosen by its chip stays until that module changes.
        /// </summary>
        private void RefreshCode()
        {
            if (_dockTab != DockTab.Code || _session == null || _listingView == null)
                return;

            CodeListing listing = _session.Listing;
            bool stepped = _session.Module != _listingModule;
            if (ReferenceEquals(listing, _shownListing) && !stepped)
                return;

            string was = _shownListing != null && _listingFile >= 0 && _listingFile < _shownListing.Files.Count
                ? _shownListing.Files[_listingFile].Path : null;
            if (stepped || was == null)
            {
                _listingModule = _session.Module;
                _listingFile = listing.FileOf(_session.Module);
            }
            else
            {
                _listingFile = -1;
                for (int i = 0; i < listing.Files.Count; i++)
                    if (listing.Files[i].Path == was)
                        _listingFile = i;
                if (_listingFile < 0)
                    _listingFile = listing.FileOf(_session.Module);
            }

            bool sameFile = _listingFile >= 0 && listing.Files[_listingFile].Path == was;
            _shownListing = listing;
            BuildCodeChips(SchematicPalette.Current);
            _listingView.Show(_listingFile >= 0 ? listing.Files[_listingFile].Text : "", sameFile);
        }

        /// <summary>A chip chosen: its file shown from the top.</summary>
        private void ChooseFile(int index)
        {
            if (_shownListing == null || index < 0 || index >= _shownListing.Files.Count)
                return;
            _listingFile = index;
            BuildCodeChips(SchematicPalette.Current);
            _listingView.Show(_shownListing.Files[index].Text, sameFile: false);
        }

        /// <summary>A chip a file, left to right in the order they are read, as many as the header has room for.</summary>
        private void BuildCodeChips(SchematicPalette palette)
        {
            _codeTools.Clear();
            if (_shownListing == null)
                return;

            float x = ToolsLeft, room = _regions.DockHeader.width - 16f;
            for (int i = 0; i < _shownListing.Files.Count; i++)
            {
                string caption = _shownListing.Caption(i);
                float width = 28f + 9f * caption.Length;
                if (x + width > room)
                    break;

                int index = i;
                ShellButton chip = ShellTheme.Button(CodeFileChip(caption), caption, palette, () => ChooseFile(index), UiType.Caption);
                ShellTheme.Place(chip, x, 7f, width, TabHeight);
                bool chosen = i == _listingFile;
                chip.style.color = chosen ? palette.Text : palette.TextDim;
                Color edge = chosen ? palette.Accent : palette.Outline;
                chip.style.borderLeftColor = chip.style.borderRightColor = chip.style.borderTopColor = chip.style.borderBottomColor = edge;
                _codeTools.Add(chip);
                x += width + ChipGap;
            }
        }

        /// <summary>The number of files the CODE tab offers a chip for, for the tests.</summary>
        public int ListingChips => _codeTools?.childCount ?? 0;

        /// <summary>A file's chip's caption, as the tests name it.</summary>
        public static string CodeFileChip(string caption) => CodeFileName + caption;
    }
}
