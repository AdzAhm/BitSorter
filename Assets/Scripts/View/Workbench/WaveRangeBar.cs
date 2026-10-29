using System;
using BitSorter.ViewModel;
using UnityEngine;
using UnityEngine.UIElements;

namespace BitSorter.View
{
    /// <summary>
    /// The waves' range bar on screen (Part 15h-4d, <see cref="WaveRange"/>), in the shape Ahmad drew from a scrollbar,
    /// laid on its side: a rounded trough for the whole run, and in it a rounded thumb for the stretch the waves show, a
    /// grip mark of three short lines at its middle. The thumb's ends drag to zoom and the thumb itself through time. It
    /// only reports a press, the drag after it and the release, as shares of its own width; the workbench moves the view
    /// and gives the bar the stretch back each frame. A shell control, so a drag on it is never the waves' own.
    /// </summary>
    public sealed class WaveRangeBar : VisualElement
    {
        /// <summary>How near an end of the thumb, in pixels, a press takes that end.</summary>
        public const float Reach = 8f;

        private const float Trough = 14f, Thumb = 10f, LeastThumb = 14f, GripLine = 1f, GripHeight = 6f, GripGap = 3f;

        private readonly VisualElement _thumb, _grip;
        private float _from, _to = 1f, _pressX;
        private bool _drawn;
        private int _pointer = -1;

        /// <summary>A press: what it took (<see cref="WaveRange.At"/>) and where, from 0 to 1 along the bar.</summary>
        public event Action<WaveRange.Grip, float> Pressed;

        /// <summary>The drag since the press, a share of the bar's width.</summary>
        public event Action<float> Moved;

        public event Action Released;

        public WaveRangeBar(string name, SchematicPalette palette)
        {
            this.name = name;
            AddToClassList(ShellTheme.ControlClass);
            focusable = false;
            pickingMode = PickingMode.Position;

            VisualElement trough = Piece("trough", palette.Over(palette.Outline, 0.22f), Trough);
            trough.style.left = 0f;
            trough.style.right = 0f;
            Add(trough);

            _thumb = Piece("thumb", palette.Outline, Thumb);
            _thumb.style.minWidth = LeastThumb;
            _thumb.style.justifyContent = Justify.Center;
            _thumb.style.alignItems = Align.Center;
            Add(_thumb);

            // The grip mark: three short lines across the thumb's middle, as a scrollbar's.
            _grip = new VisualElement { name = "grip", pickingMode = PickingMode.Ignore };
            _grip.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < 3; i++)
            {
                var line = new VisualElement { pickingMode = PickingMode.Ignore };
                line.style.width = GripLine;
                line.style.height = GripHeight;
                line.style.marginLeft = line.style.marginRight = GripGap * 0.5f;
                line.style.backgroundColor = palette.Ground;
                _grip.Add(line);
            }
            _thumb.Add(_grip);
            Show(0f, 1f);

            RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0)
                    return;
                _pointer = e.pointerId;
                this.CapturePointer(e.pointerId);
                PressAt(e.localPosition.x / Width);
                e.StopPropagation();
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != _pointer)
                    return;
                MoveBy(e.localPosition.x / Width - _pressX);
                e.StopPropagation();
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != _pointer)
                    return;
                Ended(e.pointerId);
                e.StopPropagation();
            });
            // A drag lost rather than ended ends too, or a pointer later only passing over would go on dragging.
            RegisterCallback<PointerCancelEvent>(e => Ended(e.pointerId));
            RegisterCallback<PointerCaptureOutEvent>(e => Ended(e.pointerId));
        }

        /// <summary>Where the stretch begins and ends, from 0 to 1 along the bar.</summary>
        public float From => _from;

        /// <inheritdoc cref="From"/>
        public float To => _to;

        private float Width => Mathf.Max(1f, layout.width);

        /// <summary>The thumb drawn from <paramref name="from"/> to <paramref name="to"/>, shares of the bar, when it moved.</summary>
        public void Show(float from, float to)
        {
            if (_drawn && Mathf.Approximately(from, _from) && Mathf.Approximately(to, _to))
                return;
            _drawn = true;
            _from = from;
            _to = to;
            _thumb.style.left = new Length(from * 100f, LengthUnit.Percent);
            _thumb.style.width = new Length(Mathf.Max(0f, to - from) * 100f, LengthUnit.Percent);
        }

        /// <summary>A press at <paramref name="at"/>, from 0 to 1 along the bar -- what a pointer's does, and a test's.</summary>
        public void PressAt(float at)
        {
            _pressX = Mathf.Clamp01(at);
            Pressed?.Invoke(WaveRange.At(_pressX, _from, _to, Reach / Width), _pressX);
        }

        /// <summary>The press dragged <paramref name="by"/>, a share of the bar's width.</summary>
        public void MoveBy(float by) => Moved?.Invoke(by);

        public void Release() => Released?.Invoke();

        private void Ended(int pointer)
        {
            if (pointer != _pointer)
                return;
            _pointer = -1;
            if (this.HasPointerCapture(pointer))
                this.ReleasePointer(pointer);
            Release();
        }

        /// <summary>A rounded piece, <paramref name="height"/> tall, across the bar's middle.</summary>
        private static VisualElement Piece(string pieceName, Color colour, float height)
        {
            var piece = new VisualElement { name = pieceName, pickingMode = PickingMode.Ignore };
            piece.style.position = Position.Absolute;
            piece.style.height = height;
            piece.style.top = new Length(50f, LengthUnit.Percent);
            piece.style.marginTop = -height * 0.5f;
            piece.style.backgroundColor = colour;
            float round = height * 0.5f;
            piece.style.borderTopLeftRadius = piece.style.borderTopRightRadius = piece.style.borderBottomLeftRadius =
                piece.style.borderBottomRightRadius = round;
            return piece;
        }
    }
}
