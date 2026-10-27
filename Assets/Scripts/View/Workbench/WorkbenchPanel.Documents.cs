using System.Collections.Generic;
using BitSorter.Board;
using BitSorter.ViewModel;

namespace BitSorter.View
{
    /// <summary>
    /// The student's circuits (Part 15a): the workbench shows one at a time, each kept in the save under its key
    /// (<see cref="Documents"/>). MY CIRCUIT is the first and always there; FILE makes a NEW one, OPENs another, and RENAMEs
    /// or DELETEs the one open (asking first), and an import opens as a circuit of its own. The SAMPLE chip's first place
    /// is the circuit open, as it was MY CIRCUIT's.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        private string _documentKey = WorkbenchSamples.MyCircuitKey;
        private WaveRenameField _circuitName;
        private bool _renamingCircuit;
        private DocumentMenu _openMenu;

        /// <summary>The key of the circuit open, or that the SAMPLE chip goes back to from a sample.</summary>
        public string DocumentKey => _documentKey;

        /// <summary>The field NEW and RENAME ask a circuit's name in.</summary>
        public WaveRenameField CircuitNameField => _circuitName;

        /// <summary>FILE, OPEN's list.</summary>
        public DocumentMenu OpenMenu => _openMenu;

        private void BuildDocuments(SchematicPalette palette)
        {
            _circuitName = WaveRenameField.Build(_shell, transform, palette, CircuitNamed, WaveRenameField.CircuitNameName,
                "NAME THE NEW CIRCUIT", "letters, digits and spaces");
            _openMenu = DocumentMenu.Build(_shell, transform, palette, OpenDocument);
        }

        /// <summary>Every document's key the save holds, MY CIRCUIT's whether it has been kept yet or not.</summary>
        private string[] DocumentKeys()
        {
            var keys = new List<string> { WorkbenchSamples.MyCircuitKey };
            ProgressStore store = Store();
            if (store != null)
                foreach (string key in store.ProjectKeys())
                    if (!keys.Contains(key))
                        keys.Add(key);
            return keys.ToArray();
        }

        /// <summary>The circuit under <paramref name="key"/> as the save keeps it, or a blank one named for it the first time.</summary>
        private static Project Document(string key)
        {
            ProgressStore store = Store();
            if (store != null && store.TryGetProject(key, out Project kept, new List<string>()) && kept.TopModule != null)
                return kept;
            return key == WorkbenchSamples.MyCircuitKey ? WorkbenchSamples.Blank() : Project.New(Documents.ModuleNameFor(Documents.NameOf(key)));
        }

        /// <summary>What the SAMPLE chip says for the circuit open: its name.</summary>
        private string DocumentCaption() =>
            _documentKey == WorkbenchSamples.MyCircuitKey ? SampleCaption(0) : Documents.NameOf(_documentKey).ToUpperInvariant();

        /// <summary>What the SAMPLE chip says for its place <paramref name="sample"/>: the circuit open, or a sample.</summary>
        private string ChipCaption(int sample) => sample == 0 ? DocumentCaption() : SampleCaption(sample);

        /// <summary>
        /// Opens the circuit under <paramref name="key"/>: the one open kept first, under its own key, and the new one put at
        /// the front of the save's recent list.
        /// </summary>
        public void OpenDocument(string key)
        {
            if (Root == null || !Documents.IsDocument(key))
                return;

            KeepMyCircuit(now: true);
            if (_session != null)
                _keptProject = _session.Project;   // kept: nothing of it is written under the next key
            _keepAt = -1;
            _documentKey = key;

            ProgressStore store = Store();
            if (store != null)
            {
                store.Touch(key);
                store.Save();
            }

            ShowSample(0);
        }

        /// <summary>FILE, OPEN: the circuits, the most recent first, under the FILE chip.</summary>
        public void OpenOpenMenu()
        {
            ProgressStore store = Store();
            _openMenu?.Open(UnderTheFileChip(), Documents.InOrder(DocumentKeys(), store != null ? store.Recent : null), _sample == 0 ? _documentKey : null);
        }

        /// <summary>FILE, NEW: a name asked for, a free one offered.</summary>
        public void AskNewCircuit()
        {
            _renamingCircuit = false;
            _circuitName?.Open(Documents.FreeName(DocumentKeys()), UnderTheFileChip(), "NAME THE NEW CIRCUIT");
        }

        /// <summary>FILE, RENAME: the circuit open's name asked for again; MY CIRCUIT's may not be.</summary>
        public void AskRenameCircuit()
        {
            if (_sample != 0 || !Documents.IsOwn(_documentKey))
            {
                Notice("MY CIRCUIT keeps its name: FILE, NEW makes a circuit you can name");
                return;
            }

            _renamingCircuit = true;
            _circuitName?.Open(Documents.NameOf(_documentKey), UnderTheFileChip(), "RENAME THIS CIRCUIT");
        }

        private void CircuitNamed(string name)
        {
            string why = Documents.WhyNotName(name, DocumentKeys(), _renamingCircuit ? _documentKey : null);
            if (why != null)
            {
                Notice(why);
                return;
            }

            if (_renamingCircuit)
                RenameCircuit(name);
            else
                NewCircuit(name);
        }

        /// <summary>A new, blank circuit called <paramref name="name"/>, opened; refused, saying why, for a name it may not have.</summary>
        public void NewCircuit(string name)
        {
            string why = Documents.WhyNotName(name, DocumentKeys());
            if (why != null)
            {
                Notice(why);
                return;
            }

            name = name.Trim();
            string key = Documents.KeyOf(name);
            ProgressStore store = Store();
            if (store != null)
                store.SetProject(key, Project.New(Documents.ModuleNameFor(name)));
            OpenDocument(key);
            Notice("made the circuit " + name + ": FILE, OPEN goes back to any other");
        }

        /// <summary>The circuit open renamed <paramref name="name"/>; refused, saying why, for MY CIRCUIT or a name it may not have.</summary>
        public void RenameCircuit(string name)
        {
            string why = !Documents.IsOwn(_documentKey) ? "MY CIRCUIT keeps its name." : Documents.WhyNotName(name, DocumentKeys(), _documentKey);
            if (why != null)
            {
                Notice(why);
                return;
            }

            KeepMyCircuit(now: true);
            name = name.Trim();
            string key = Documents.KeyOf(name);
            ProgressStore store = Store();
            if (store == null || !store.RenameProject(_documentKey, key))
            {
                Notice("it could not be renamed: the save holds no circuit by its old name");
                return;
            }

            store.Save();
            _documentKey = key;
            SetText(_sampleButton, DocumentCaption());
            Notice("renamed it " + name);
        }

        /// <summary>FILE, DELETE: asks before the circuit open goes; MY CIRCUIT may not.</summary>
        public void AskDeleteCircuit()
        {
            if (_sample != 0 || !Documents.IsOwn(_documentKey))
            {
                Notice("MY CIRCUIT is always there, so it cannot be deleted");
                return;
            }

            string name = Documents.NameOf(_documentKey);
            _ask.Ask(UnderTheFileChip(), "Delete the circuit " + name + "? It cannot be brought back.", "DELETE " + name.ToUpperInvariant(), DeleteCircuit);
        }

        /// <summary>The circuit open taken out of the save, and MY CIRCUIT opened in its place.</summary>
        public void DeleteCircuit()
        {
            if (!Documents.IsOwn(_documentKey))
                return;

            string name = Documents.NameOf(_documentKey);
            ProgressStore store = Store();
            if (store != null)
            {
                store.RemoveProject(_documentKey);
                store.Save();
            }

            if (_session != null)
                _keptProject = _session.Project;   // gone with its key: nothing of it is written under MY CIRCUIT's
            _keepAt = -1;
            OpenDocument(WorkbenchSamples.MyCircuitKey);
            Notice("deleted the circuit " + name);
        }
    }
}
