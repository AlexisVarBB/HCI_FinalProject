mergeInto(LibraryManager.library, {
  // Inicializa Firebase Auth en cuanto se muestra el Login.
  // En el celular, la ventana de Google solo se permite si se abre justo en el toque;
  // si Firebase se inicializara en ese momento, tardaría demasiado y el navegador la bloquearía.
  DetoxGoogleInit: function (apiKeyPtr, authDomainPtr) {
    if (typeof firebase === "undefined" || !firebase.auth) return;
    if (firebase.apps.length) return;
    firebase.initializeApp({ apiKey: UTF8ToString(apiKeyPtr), authDomain: UTF8ToString(authDomainPtr) });
    // La sesión la guarda Unity; el SDK de JS no necesita recordarla.
    firebase.auth().setPersistence(firebase.auth.Auth.Persistence.NONE);
  },

  // Prepara el inicio con Google. Unity lo llama en el pointerdown del botón;
  // la ventana se abre al soltar (pointerup / touchend), que es un gesto real del usuario.
  // Al terminar responde a Unity con SendMessage(gameObject, method, json).
  // Solo se envía a Unity: uid, tokens y nombre. No se usa la foto ni el correo.
  DetoxGoogleArm: function (apiKeyPtr, authDomainPtr, gameObjectPtr, methodPtr) {
    var apiKey = UTF8ToString(apiKeyPtr);
    var authDomain = UTF8ToString(authDomainPtr);
    var gameObject = UTF8ToString(gameObjectPtr);
    var method = UTF8ToString(methodPtr);
    var send = (typeof SendMessage !== "undefined") ? SendMessage : Module.SendMessage;

    function reply(data) { send(gameObject, method, JSON.stringify(data)); }

    if (typeof firebase === "undefined" || !firebase.auth) {
      reply({ error: "firebase-sdk-missing" });
      return;
    }

    if (!firebase.apps.length) {
      firebase.initializeApp({ apiKey: apiKey, authDomain: authDomain });
      firebase.auth().setPersistence(firebase.auth.Auth.Persistence.NONE);
    }

    // Si se armó antes y no se usó, se reemplaza.
    if (window.__detoxGoogleRun) {
      document.removeEventListener("pointerup", window.__detoxGoogleRun, true);
      document.removeEventListener("touchend", window.__detoxGoogleRun, true);
    }

    var fired = false;
    var run = function () {
      if (fired) return;
      fired = true;
      document.removeEventListener("pointerup", run, true);
      document.removeEventListener("touchend", run, true);
      window.__detoxGoogleRun = null;

      var provider = new firebase.auth.GoogleAuthProvider();
      provider.setCustomParameters({ prompt: "select_account" });

      // Se llama directo dentro del gesto (sin esperar nada antes) para que no se bloquee.
      firebase.auth().signInWithPopup(provider).then(function (result) {
        var user = result.user;
        return user.getIdTokenResult().then(function (token) {
          var expiresIn = Math.max(60, Math.floor((Date.parse(token.expirationTime) - Date.now()) / 1000));
          reply({
            uid: user.uid,
            idToken: token.token,
            refreshToken: user.refreshToken,
            expiresIn: expiresIn,
            name: user.displayName || ""
          });
        });
      }).catch(function (error) {
        reply({ error: (error && error.code) ? error.code : String(error) });
      });
    };

    window.__detoxGoogleRun = run;
    document.addEventListener("pointerup", run, true);
    document.addEventListener("touchend", run, true);
  }
});
