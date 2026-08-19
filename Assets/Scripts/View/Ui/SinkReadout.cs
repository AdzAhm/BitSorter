using System.Collections.Generic;
using System.Text;
using BitSorter.LogicCore;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BitSorter.View
{
    /// <summary>
    /// What each sink has caught, in order, while a free-play run happens and after it stops.
    /// </summary>
    /// <remarks>
    /// Free play only, and that is the whole justification for it. A graded level already tells the
    /// player what a sink was supposed to receive, and the verdict says whether it did; showing the
    /// raw catch there would be a third account of the same thing. In a sandbox there is no intended
    /// answer, so what came out is the only result there is.
    ///
    /// Derived every frame from <see cref="SinkNode.Received"/> rather than accumulated here. The
    /// simulation already records each bit with the tick it landed on, and a second copy kept in the
    /// interface would be a second thing to drift -- and would have to be told about resets.
    ///
    /// Updated while running, not only at the end, because watching bits arrive one tick at a time is
    /// how the delay lesson reads.
    /// </remarks>
    public sealed class SinkReadout : MonoBehaviour
    {
        private sealed class Row
        {
            public string SinkId;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Bits;
        }

        [SerializeField] private LevelSession _session;
        [SerializeField] private SimulationRunner _runner;

        [Tooltip("Canvas the readout is built under. Found by type when left empty.")]
        [SerializeField] private Canvas _canvas;

        private readonly List<Row> _rows = new List<Row>();
        private readonly StringBuilder _text = new StringBuilder(32);

        private RectTransform _root;
        private RectTransform _list;
        private string _built;

        private void Awake()
        {
            if (_session == null) _session = FindFirstObjectByType<LevelSession>();
            if (_runner == null) _runner = FindFirstObjectByType<SimulationRunner>();
            if (_canvas == null) _canvas = FindFirstObjectByType<Canvas>();
        }

        private void Start()
        {
            if (_canvas == null || _session == null)
                return;

            Build();
        }

        private void Update()
        {
            if (_root == null)
                return;

            LevelDefinition level = _session.Level;
            bool wanted = level != null && !level.IsGraded && _runner != null && _runner.IsReady;

            if (_root.gameObject.activeSelf != wanted)
                _root.gameObject.SetActive(wanted);

            if (!wanted)
                return;

            string signature = Signature(level);

            if (signature != _built)
                Rebuild(level, signature);

            Refresh();
        }

        // -----------------------------------------------------------------
        // Building
        // -----------------------------------------------------------------

        private void Build()
        {
            Image panel = UiTheme.Panel_("Sink readout", _canvas.transform, UiTheme.Panel);
            _root = panel.GetComponent<RectTransform>();

            UiTheme.Anchor(_root, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-16f, 16f), new Vector2(230f, 130f));

            TextMeshProUGUI title = UiTheme.Label(
                "title", _root, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
            UiTheme.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -8f), new Vector2(200f, 18f));
            title.text = "CAUGHT";

            _list = UiTheme.Rect("rows", _root);
            UiTheme.Anchor(_list, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -30f), new Vector2(206f, 96f));

            _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// The sinks, in order, as one string. Cheap to compare per frame, and it changes exactly when
        /// the rows would need rebuilding -- which in free play is whenever a sink is added or removed.
        /// </summary>
        private static string Signature(LevelDefinition level)
        {
            var text = new StringBuilder();

            for (int i = 0; i < level.Fixtures.Count; i++)
            {
                if (level.Fixtures[i].Kind != FixtureKind.Sink)
                    continue;

                text.Append(level.Fixtures[i].Id).Append('|');
            }

            return text.ToString();
        }

        private void Rebuild(LevelDefinition level, string signature)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Name != null)
                    Destroy(_rows[i].Name.gameObject);

                if (_rows[i].Bits != null)
                    Destroy(_rows[i].Bits.gameObject);
            }

            _rows.Clear();

            const float height = 20f;
            int index = 0;

            for (int i = 0; i < level.Fixtures.Count; i++)
            {
                LevelFixture fixture = level.Fixtures[i];

                if (fixture.Kind != FixtureKind.Sink)
                    continue;

                var row = new Row { SinkId = fixture.Id };

                row.Name = UiTheme.Label(
                    fixture.Id, _list, 13f, UiTheme.TextDim, TextAlignmentOptions.Left);
                UiTheme.Anchor(row.Name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, -index * height), new Vector2(70f, height));
                row.Name.text = fixture.Id;

                row.Bits = UiTheme.Label(
                    $"{fixture.Id} bits", _list, 14f, UiTheme.Text, TextAlignmentOptions.Left);
                UiTheme.Anchor(row.Bits.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(74f, -index * height), new Vector2(132f, height));

                _rows.Add(row);
                index++;
            }

            // Grown to fit rather than scrolled: the board caps sinks at one column, so this is a
            // handful of rows at most.
            _root.sizeDelta = new Vector2(230f, 46f + Mathf.Max(1, index) * height);
            _built = signature;
        }

        // -----------------------------------------------------------------
        // Reading
        // -----------------------------------------------------------------

        private void Refresh()
        {
            SimulationView view = _runner.View;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];

                _text.Clear();

                if (TryFindSink(view, row.SinkId, out SinkNode sink))
                {
                    IReadOnlyList<SinkNode.Reception> caught = sink.Received;

                    for (int k = 0; k < caught.Count; k++)
                    {
                        if (k > 0)
                            _text.Append(' ');

                        _text.Append((int)caught[k].Value);
                    }
                }

                // An em dash rather than an empty line, so "nothing arrived" is a statement and not a
                // gap the player has to interpret.
                row.Bits.text = _text.Length > 0 ? _text.ToString() : "--";
                row.Bits.color = _text.Length > 0 ? UiTheme.Text : UiTheme.TextDim;
            }
        }

        private bool TryFindSink(SimulationView view, string sinkId, out SinkNode sink)
        {
            sink = null;

            if (!_runner.FixtureNodeIds.TryGetValue(sinkId, out int nodeId))
                return false;

            if (nodeId < 0 || nodeId >= view.NodeCount)
                return false;

            sink = view.GetNode(nodeId) as SinkNode;
            return sink != null;
        }
    }
}
