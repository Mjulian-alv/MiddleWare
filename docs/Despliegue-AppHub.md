<!-- Fecha: 02/11/2026 - TAREA: 00157 - Linea: 4 - Puesta en marcha de DataPipeline.Admin detras de AppHub. -->
# Puesta en marcha: DataPipeline.Admin detrás de AppHub

Diseño: `docs/Diseño/00157-identidad-apphub/identidad-apphub.md`. Contrato: `Docs/Contrato-Apps.md` de AppHub (v0.2).

**Qué cambia:** quien AppHub deja abrir la app `datapipeline` entra al Admin **como administrador, sin pedir
usuario ni contraseña**. El Admin no tiene usuarios ni permisos propios, así que **quién puede abrirlo lo
decide solo AppHub** («Usuarios» → asignar la app `datapipeline`). Es una excepción al punto 4 del contrato
(«el primer ingreso nunca da permisos»), pensada porque el Admin es todo-o-nada.

`DataPipeline.Api` no cambia: el Admin le sigue hablando con Basic desde el servidor.

## 1. Clave compartida

Una cadena aleatoria de **al menos 32 caracteres**, la misma en los dos lados. No va en el repo.

## 2. Config del Admin (`appsettings.json` del servidor, o variables de entorno)

```json
"PathBase": "/datapipeline",
"ForzarHttps": false,
"AppHub": {
  "Aplicacion": "datapipeline",
  "ClaveCompartida": "<la clave del paso 1>",
  "ToleranciaSegundos": 30
}
```

- `PathBase`: el prefijo bajo el que AppHub lo publica. **Tiene que coincidir con `Ruta` de AppHub.**
- `ForzarHttps: false`: detrás de AppHub el Admin recibe http por localhost; con `true` redirigiría en bucle.
- `ClaveCompartida` vacía = **modo autónomo**: el Admin funciona exactamente como antes (login `dpAdmin`).
- Con clave y sin `Aplicacion`, o con una clave de menos de 32 caracteres, **el Admin no arranca** (a propósito).
- Con `PathBase` el Admin **deja de verse bien por su dirección vieja** (los enlaces apuntan al prefijo). Si todavía
  hay gente entrando directo, mantené una copia aparte con `PathBase` vacío y sin clave.

## 3. Alta en AppHub (`Apps[]` de su `appsettings.json`)

```json
{
  "Clave": "datapipeline",
  "Nombre": "DataPipeline",
  "Descripcion": "Pipelines, orígenes y envíos de datos.",
  "Color": "#6366f1",
  "Ruta": "/datapipeline",
  "Destino": "http://127.0.0.1:<puerto del Admin>",
  "IdentidadClave": "<la clave del paso 1>"
}
```

`Clave` tiene que ser igual a `AppHub:Aplicacion` del Admin (es el destinatario `aud` del token).

## 4. Lo que AppHub tiene que cumplir (sin probar contra el AppHub real)

1. **Agregar `X-AppHub-Identidad` a todos los pedidos que reenvía, incluidos la negociación y el websocket de
   `/_blazor`.** El Admin es Blazor Server y no usa cookie: la identidad llega al circuito a través del pedido que lo
   crea. Si el websocket no lleva el encabezado, la persona cae en el login propio.
2. **Conservar el `Host` original.** Blazor arma las redirecciones con el host del pedido y el Admin **no procesa
   `X-Forwarded-*`**; con el host interno la persona saldría del portal.

## 5. Probar

1. Abrí `https://<portal>/datapipeline/`: no tiene que pedir usuario ni contraseña, y se ve tu correo arriba
   **sin botón «Salir»** (se sale desde AppHub).
2. Recargá la página: tiene que entrar de nuevo sola.
3. Entrá directo a `https://<portal>/datapipeline/login`: con sesión de AppHub te lleva al inicio.
4. Una persona sin la app asignada no llega al Admin (la frena AppHub).
5. En `logs\dp-admin-*.log` aparece `Ingreso por AppHub: <correo>`. Un token malo deja
   `Identidad de AppHub rechazada: <motivo>` (nunca el token ni la clave) y el login muestra
   «AppHub no pudo confirmar tu identidad».

## 6. Volver al modo autónomo

`ClaveCompartida` vacía y reiniciar. El login `dpAdmin` sigue funcionando.
