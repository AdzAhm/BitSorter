using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BitSorter.View
{
    /// <summary>
    /// Whether the pointer is over one of the workbench's controls -- a button, the slider, the row
    /// menu -- sampled before anything reacts to the frame's input, as <see cref="PointerGate"/>
    /// samples "over the interface" for the board (Part 9b-2b).
    /// </summary>
    /// <remarks>
    /// The workbench is a full-screen panel, so the pointer is over its interface everywhere and
    /// <see cref="PointerGate"/>'s question cannot tell a button from the waves. This asks the narrower
    /// one: is the topmost thing under the pointer something with clicks of its own. **Sampled at the
    /// frame's start**, because a tap puts press and release in one frame and the event system handles
    /// the whole click before the panel's Update may run: a click on the menu that closes it would
    /// otherwise leave the waves under the pointer by the time the panel asks, and seek as well.
    /// </remarks>
    [DefaultExecutionOrder(-30000)]
    internal sealed class WorkbenchControlSample : MonoBehaviour
    {
        private readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private PointerEventData _probe;
        private int _sampledFrame = -1;
        private bool _overAtFrameStart;

        /// <summary>Whether the pointer was over a control as the frame began, or is now.</summary>
        public bool OverControl => (_sampledFrame == Time.frameCount && _overAtFrameStart) || OverControlNow();

        private void Update()
        {
            _overAtFrameStart = OverControlNow();
            _sampledFrame = Time.frameCount;
        }

        /// <summary>Whether the topmost thing under the pointer, at this moment, has clicks of its own.</summary>
        public bool OverControlNow()
        {
            EventSystem events = EventSystem.current;
            Mouse mouse = Mouse.current;
            if (events == null || mouse == null)
                return false;

            // The shell's controls (Part 11a): the event system sees its panel as one nameless object, so the
            // panel is asked which of its elements is under the pointer.
            if (ShellPanel.OverControlAt(mouse.position.ReadValue()))
                return true;

            if (_probe == null)
                _probe = new PointerEventData(events);
            _probe.Reset();
            _probe.position = mouse.position.ReadValue();

            _hits.Clear();
            events.RaycastAll(_probe, _hits);
            if (_hits.Count == 0)
                return false;

            GameObject top = _hits[0].gameObject;
            return top.GetComponentInParent<Selectable>() != null || top.GetComponentInParent<WaveMenu>() != null
                || top.GetComponentInParent<WaveSignalPicker>() != null || top.GetComponentInParent<WaveRenameField>() != null
                || top.GetComponentInParent<CodeEditorView>() != null;
        }
    }
}
