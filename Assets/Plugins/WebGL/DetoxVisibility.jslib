mergeInto(LibraryManager.library, {
  // Avisa a Unity cuando la pestaña se oculta o vuelve a verse
  // (cambiar de pestaña, minimizar, o en el celular irse a otra app).
  DetoxRegisterVisibility: function (gameObjectPtr, methodPtr) {
    var gameObject = UTF8ToString(gameObjectPtr);
    var method = UTF8ToString(methodPtr);
    var send = (typeof SendMessage !== "undefined") ? SendMessage : Module.SendMessage;

    document.addEventListener("visibilitychange", function () {
      send(gameObject, method, document.hidden ? "hidden" : "visible");
    });
  }
});
