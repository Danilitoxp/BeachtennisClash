using BeachTennis.Simulation;
using UnityEngine;

namespace BeachTennis.Presentation
{
    /// <summary>
    /// Fits the gray-box court geometry to the rules in use: singles (4.5 m) or doubles (8 m) width and
    /// net height by category. ITF R1: measurements are to the outside of the lines, so lines are drawn
    /// inside the court edge.
    /// </summary>
    public sealed class CourtView : MonoBehaviour
    {
        [SerializeField] private Transform sidelineWest;
        [SerializeField] private Transform sidelineEast;
        [SerializeField] private Transform baselineSouth;
        [SerializeField] private Transform baselineNorth;
        [SerializeField] private Transform net;
        [SerializeField] private Transform netTape;
        [SerializeField] private Transform postWest;
        [SerializeField] private Transform postEast;

        public void Apply(in CourtDimensions court)
        {
            float lw = court.LineWidth;
            float x = court.HalfWidth - lw * 0.5f;
            float z = court.HalfLength - lw * 0.5f;
            const float y = 0.003f;

            Place(sidelineWest, new Vector3(-x, y, 0f), new Vector3(lw, 0.004f, court.Length));
            Place(sidelineEast, new Vector3(x, y, 0f), new Vector3(lw, 0.004f, court.Length));
            Place(baselineSouth, new Vector3(0f, y, -z), new Vector3(court.Width, 0.004f, lw));
            Place(baselineNorth, new Vector3(0f, y, z), new Vector3(court.Width, 0.004f, lw));

            float span = court.Width + 2f * court.NetPostOffset;
            const float band = 1.0f;
            Place(net, new Vector3(0f, court.NetHeight - band * 0.5f, 0f), new Vector3(span, band, 0.02f));
            Place(netTape, new Vector3(0f, court.NetHeight - 0.025f, 0f), new Vector3(span, 0.05f, 0.03f));
            float postHeight = court.NetHeight + 0.1f;
            Place(postWest, new Vector3(-span * 0.5f, postHeight * 0.5f, 0f), new Vector3(0.08f, postHeight * 0.5f, 0.08f));
            Place(postEast, new Vector3(span * 0.5f, postHeight * 0.5f, 0f), new Vector3(0.08f, postHeight * 0.5f, 0.08f));
        }

        private static void Place(Transform t, Vector3 position, Vector3 scale)
        {
            if (t == null) return;
            t.position = position;
            t.localScale = scale;
        }
    }
}
