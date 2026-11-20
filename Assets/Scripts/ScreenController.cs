using UnityEngine.UIElements;

namespace DetoxBet.UI
{
    /// <summary>
    /// Base de todas las pantallas. No es un componente: la crea y la controla ScreenManager.
    /// </summary>
    public abstract class ScreenController
    {
        /// <summary>Se llama cada vez que la pantalla aparece, con su árbol recién creado.</summary>
        public abstract void OnShow(VisualElement root);

        /// <summary>Se llama cuando la pantalla se va (para quitar callbacks y listeners).</summary>
        public virtual void OnHide() { }

        /// <summary>La app pasó a segundo plano (true) o regresó (false). Solo la recibe la pantalla visible.</summary>
        public virtual void OnApplicationPause(bool paused) { }
    }
}
