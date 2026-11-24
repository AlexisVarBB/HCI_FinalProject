using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    
    [UxmlElement]
    public partial class ShieldBoltIcon : VisualElement
    {
        Color m_Color = new Color(0f, 0.94f, 0.54f, 1f);
        float m_Thickness = 1.8f;

        [UxmlAttribute]
        public Color IconColor
        {
            get => m_Color;
            set { m_Color = value; MarkDirtyRepaint(); }
        }

        [UxmlAttribute]
        public float Thickness
        {
            get => m_Thickness;
            set { m_Thickness = value; MarkDirtyRepaint(); }
        }

        public ShieldBoltIcon()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float size = Mathf.Min(contentRect.width, contentRect.height);
            if (size < 1f) return;

            float s = size / 24f;
            var off = new Vector2((contentRect.width - size) * 0.5f, (contentRect.height - size) * 0.5f);
            Vector2 P(float x, float y) => off + new Vector2(x * s, y * s);

            var p = ctx.painter2D;
            p.strokeColor = m_Color;
            p.lineWidth = m_Thickness * s;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

          
            p.BeginPath();
            p.MoveTo(P(12f, 2.5f));
            p.BezierCurveTo(P(14.5f, 4.4f), P(17f, 5.2f), P(19.5f, 5.4f));
            p.LineTo(P(19.5f, 12f));
            p.BezierCurveTo(P(19.5f, 16.8f), P(16.2f, 19.8f), P(12f, 21.5f));
            p.BezierCurveTo(P(7.8f, 19.8f), P(4.5f, 16.8f), P(4.5f, 12f));
            p.LineTo(P(4.5f, 5.4f));
            p.BezierCurveTo(P(7f, 5.2f), P(9.5f, 4.4f), P(12f, 2.5f));
            p.ClosePath();
            p.Stroke();

         
            p.BeginPath();
            p.MoveTo(P(13f, 7.5f));
            p.LineTo(P(9.5f, 12.5f));
            p.LineTo(P(12.2f, 12.5f));
            p.LineTo(P(11f, 16.5f));
            p.LineTo(P(14.5f, 11.5f));
            p.LineTo(P(11.8f, 11.5f));
            p.ClosePath();
            p.Stroke();
        }
    }
}
