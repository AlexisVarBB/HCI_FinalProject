using UnityEngine;
using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    [UxmlElement]
    public partial class RadialGlow : VisualElement
    {
        Color m_Color = new Color(0f, 0.94f, 0.54f, 0.22f);

        [UxmlAttribute]
        public Color GlowColor
        {
            get => m_Color;
            set { m_Color = value; MarkDirtyRepaint(); }
        }

        public RadialGlow()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerateVisualContent;
        }

        void OnGenerateVisualContent(MeshGenerationContext ctx)
        {
            float w = contentRect.width;
            float h = contentRect.height;
            if (w < 1f || h < 1f) return;

            var center = new Vector2(w * 0.5f, h * 0.5f);
            float radius = Mathf.Min(w, h) * 0.5f;

            var transparent = m_Color;
            transparent.a = 0f;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(m_Color, 0f), new GradientColorKey(m_Color, 1f) },
                new[]
                {
                    new GradientAlphaKey(m_Color.a, 0f),
                    new GradientAlphaKey(m_Color.a * 0.45f, 0.4f),
                    new GradientAlphaKey(0f, 1f)
                });

            var painter = ctx.painter2D;
            painter.fillGradient = FillGradient.MakeRadialGradient(
                gradient, center, radius, center, AddressMode.Clamp);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f), ArcDirection.Clockwise);
            painter.ClosePath();
            painter.Fill();
        }
    }
}
