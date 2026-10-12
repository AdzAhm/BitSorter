using System;
using System.Collections.Generic;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// A wire as the schematic draws it: straight runs between corners, every bend rounded, sampled
    /// into short straight pieces so a change can be put anywhere along it.
    /// </summary>
    /// <remarks>
    /// In the stage's own units, y down, as the layout is written. A bend is the quadratic curve the
    /// mock drew -- from <see cref="CornerRadius"/> before the corner, pulled towards it, to as far
    /// after -- so the two are the same shape and a change sits at the same place on both.
    /// </remarks>
    public sealed class Route
    {
        /// <summary>How far before and after a corner its bend begins and ends.</summary>
        public const float CornerRadius = 8f;

        /// <summary>How many straight pieces a bend is drawn with: enough that none shows as a facet.</summary>
        private const int CornerSteps = 8;

        private readonly Vector2[] _points;
        private readonly float[] _distances;

        public Route(params Vector2[] corners)
        {
            if (corners == null || corners.Length < 2)
                throw new ArgumentException("A route needs at least two points.", nameof(corners));

            var points = new List<Vector2> { corners[0] };

            for (int i = 1; i < corners.Length - 1; i++)
            {
                Vector2 previous = corners[i - 1], corner = corners[i], next = corners[i + 1];
                float into = Vector2.Distance(previous, corner), outOf = Vector2.Distance(corner, next);

                if (into < 1e-4f || outOf < 1e-4f)
                {
                    Add(points, corner);
                    continue;
                }

                Vector2 entry = corner + (previous - corner) * (Mathf.Min(CornerRadius, into * 0.5f) / into);
                Vector2 exit = corner + (next - corner) * (Mathf.Min(CornerRadius, outOf * 0.5f) / outOf);

                for (int s = 0; s <= CornerSteps; s++)
                {
                    float u = s / (float)CornerSteps;
                    Add(points, (1f - u) * (1f - u) * entry + 2f * (1f - u) * u * corner + u * u * exit);
                }
            }

            Add(points, corners[corners.Length - 1]);

            _points = points.ToArray();
            _distances = new float[_points.Length];

            for (int i = 1; i < _points.Length; i++)
                _distances[i] = _distances[i - 1] + Vector2.Distance(_points[i - 1], _points[i]);

            Length = _distances[_distances.Length - 1];
        }

        /// <summary>The route's length along all its runs and bends.</summary>
        public float Length { get; }

        /// <summary>Where it begins: the driver's end.</summary>
        public Vector2 Start => _points[0];

        /// <summary>Where it ends: the load's end.</summary>
        public Vector2 End => _points[_points.Length - 1];

        /// <summary>How many points it is sampled into.</summary>
        public int PointCount => _points.Length;

        /// <summary>One of its sampled points, in order from the start.</summary>
        public Vector2 Point(int index) => _points[index];

        /// <summary>How far along the route a sampled point is.</summary>
        public float DistanceAt(int index) => _distances[index];

        /// <summary>The point <paramref name="distance"/> along the route, clamped to its ends.</summary>
        public Vector2 PointAt(float distance)
        {
            if (distance <= 0f) return _points[0];
            if (distance >= Length) return End;

            int low = 0, high = _distances.Length - 1;
            while (high - low > 1)
            {
                int mid = (low + high) >> 1;
                if (_distances[mid] <= distance) low = mid;
                else high = mid;
            }

            float span = _distances[high] - _distances[low];
            float u = span > 0f ? (distance - _distances[low]) / span : 0f;
            return Vector2.LerpUnclamped(_points[low], _points[high], u);
        }

        private static void Add(List<Vector2> points, Vector2 point)
        {
            if ((points[points.Count - 1] - point).sqrMagnitude > 1e-8f)
                points.Add(point);
        }
    }
}
