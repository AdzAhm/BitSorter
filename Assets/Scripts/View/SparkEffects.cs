using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// One shared ParticleSystem that bursts sparks anywhere on demand.
    /// </summary>
    /// <remarks>
    /// A single system emitting at arbitrary positions via EmitParams, rather than one system per
    /// effect, so there is nothing to pool and only one configuration to get right.
    ///
    /// This is the one part of the visual pass configured entirely from code that cannot be run
    /// outside the editor, and a misconfigured ParticleSystem fails by rendering nothing rather
    /// than by erroring. If sparks are missing, this component is the place to look: the usual
    /// culprits are the renderer material, the sorting order, or emission being left enabled.
    /// </remarks>
    public sealed class SparkEffects : MonoBehaviour
    {
        [SerializeField] private int _sparksPerBurst = 8;
        [SerializeField] private float _lifetime = 0.38f;
        [SerializeField] private float _speed = 2.6f;
        [SerializeField] private float _size = 0.13f;

        private ParticleSystem _system;

        private void Awake()
        {
            Build();
        }

        /// <summary>Fires a burst of sparks at a world position.</summary>
        public void Burst(Vector2 position, Color colour)
        {
            if (_system == null)
                return;

            var parameters = new ParticleSystem.EmitParams
            {
                position = position,
                applyShapeToPosition = true,
                startColor = colour,
            };

            _system.Emit(parameters, _sparksPerBurst);
        }

        private void Build()
        {
            var host = new GameObject("Sparks");
            host.transform.SetParent(transform, false);

            _system = host.AddComponent<ParticleSystem>();

            // Stop it before configuring: a fresh ParticleSystem plays on awake and would spray
            // its defaults across the scene for a frame.
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = _system.main;
            main.duration = 1f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = _lifetime;
            main.startSpeed = _speed;
            main.startSize = _size;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            // Emission only ever happens through Emit(), never on a timer.
            ParticleSystem.EmissionModule emission = _system.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = _system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.06f;

            ParticleSystem.ColorOverLifetimeModule fade = _system.colorOverLifetime;
            fade.enabled = true;
            fade.color = new ParticleSystem.MinMaxGradient(FadeGradient());

            ParticleSystem.SizeOverLifetimeModule shrink = _system.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, ShrinkCurve());

            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = SparkMaterial();
            renderer.sortingOrder = 4;   // above bits

            _system.Play();
        }

        private static Gradient FadeGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.35f), new GradientAlphaKey(0f, 1f) });

            return gradient;
        }

        private static AnimationCurve ShrinkCurve() =>
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));

        private static Material SparkMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent");
            var material = new Material(shader);

            Sprite dot = ProceduralSprites.Dot();
            if (dot != null)
                material.mainTexture = dot.texture;

            return material;
        }
    }
}
