using UnityEngine;

namespace TractorBeam
{
    internal enum BeamState { Off, Searching, Locked, Unpowered }

    internal readonly struct BeamFrame
    {
        internal readonly Vector3 Origin, End, Direction;
        internal readonly float GridSize;
        internal readonly BeamState State;

        internal BeamFrame(Vector3 origin, Vector3 end, Vector3 direction, float gridSize, BeamState state)
        {
            Origin = origin;
            End = end;
            Direction = direction;
            GridSize = gridSize;
            State = state;
        }

        internal BeamFrame WithState(BeamState state) => new BeamFrame(Origin, End, Direction, GridSize, state);
    }

    internal sealed class BeamFeedback
    {
        private LineRenderer line;
        private Material material;

        internal void Show(BeamFrame frame, Shader shader)
        {
            if (frame.State == BeamState.Off)
            {
                Hide();
                return;
            }

            if (line == null)
            {
                var visual = new GameObject("Tractor Beam Feedback");
                line = visual.AddComponent<LineRenderer>();
                line.enabled = false;
                line.useWorldSpace = true;
                line.positionCount = 2;
                material = new Material(shader);
                line.sharedMaterial = material;
            }

            float wave = 0.5f + 0.5f * Mathf.Sin(Time.time * 19f);
            float width = Mathf.Clamp(frame.GridSize * 0.1f, 0.06f, 0.45f);
            line.widthMultiplier = width * (0.65f + 0.35f * wave);
            line.SetPosition(0, frame.Origin);
            Vector3 end = frame.End;
            Color color;
            switch (frame.State)
            {
                case BeamState.Locked:
                    color = new Color(0.10f, 0.95f, 1f, 1f);
                    break;
                case BeamState.Unpowered:
                    color = new Color(1f, 0.10f, 0.07f, 1f);
                    end = frame.Origin + frame.Direction * Mathf.Min(
                        Vector3.Distance(frame.Origin, frame.End), Mathf.Clamp(frame.GridSize * 2, 0.5f, 4f));
                    break;
                default:
                    color = new Color(1f, 0.55f, 0.06f, 0.8f);
                    break;
            }
            line.SetPosition(1, end);
            material.color = color * (0.75f + 0.25f * wave);
            line.enabled = true;
        }

        internal void Hide()
        {
            if (line != null) line.enabled = false;
        }

        internal void Dispose()
        {
            if (line != null) Object.Destroy(line.gameObject);
            if (material != null) Object.Destroy(material);
            line = null;
            material = null;
        }
    }
}
