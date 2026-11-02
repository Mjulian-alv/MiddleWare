# TAREA 00157 — DataPipeline.Admin dentro de AppHub (identidad)

Fecha: 02/11/2026 · Estado: construido y verificado en navegador (ver §9) · Basada en el contrato
`D:\Sistemas\Concentrador\Docs\Contrato-Apps.md` (v0.2) y en el piloto del Conciliador (TAREA 00140).

## 1. Qué es

AppHub (portal con ingreso por Microsoft) deja abrir DataPipeline.Admin sin que pida su
usuario y contraseña. AppHub manda un token firmado en el encabezado `X-AppHub-Identidad`;
el Admin lo valida y abre la sesión. Sin la clave compartida el Admin funciona **igual que
hoy** (modo autónomo, login `dpAdmin`).

Alcance: **solo DataPipeline.Admin**. `DataPipeline.Api` no cambia: el Admin le sigue
hablando con Basic, desde el servidor.

## 2. Ítems del pedido

Pedido textual: «modificar el proyecto D:\Sistemas\MiddleWare para que se integre con
AppHub… mismo trabajo que se hizo en Conciliador». Numeración (confirmada: «igual que
Conciliador»):

| Ítem | Qué |
|---|---|
| 1 | Sección `AppHub` en la config y validación del token `X-AppHub-Identidad` (HS256, `iss`, `aud`, vencimiento, correo). |
| 2 | Usuario de AppHub: quien llega con token válido entra **como administrador** (ver §4, decisión 1). |
| 3 | Sesión: entra sin login; con sesión de AppHub no se muestra «Salir»; el login propio queda como respaldo con el aviso «AppHub no pudo confirmar tu identidad». |
| 4 | Correr detrás de AppHub: `PathBase` (`/datapipeline`), sin redirección a https, cookies/websocket de Blazor; guía de despliegue. |

## 3. Cómo funciona el Admin hoy (lo que condiciona el diseño)

- Un único usuario fijo `dpAdmin`; contraseña BCrypt en `admin-credentials.json`
  (`AdminAuthService`). **No hay tabla de usuarios ni permisos.**
- La sesión es `AdminSessionState`, scoped por **circuito Blazor Server** (memoria; **sin
  cookie**). Una recarga completa pide el login de nuevo.
- `MainLayout` muestra la app solo si `Session.IsAuthenticated`; si no, manda a `/login`.

Consecuencia: el Conciliador abre una cookie desde un middleware; acá no hay cookie y no
conviene inventarla. Una conexión Blazor se crea a partir del **primer pedido HTTP**, que sí
trae el encabezado (AppHub lo agrega en cada pedido); los mensajes posteriores viajan por el
websocket y ya no lo traen.

## 4. Decisiones

1. **Permisos: quien AppHub deja abrir, entra como administrador.** Es una **excepción al
   punto 4 del contrato** («el primer ingreso nunca da permisos»): el Admin es todo-o-nada y
   no tiene dónde guardar permisos. Quien decide el acceso es AppHub (asignación de la app
   `datapipeline` en «Usuarios»). Se registra en el log el correo que entró. *Decidido por
   quien hizo el pedido.*
2. **Sin tabla de usuarios, sin `oid` persistido.** El correo y el nombre del token alcanzan
   para mostrar quién está; no se guarda nada. `AdministradoresIniciales` **no se usa** (no hay
   nada que ascender) y no se agrega a la config de esta app.
3. **Sin cookie.** El middleware valida el token del pedido HTTP y deja la identidad como
   claims en `HttpContext.User` (un principal **sin tipo de autenticación**: para ASP.NET no
   está autenticado, no abre nada por sí solo). Blazor toma el usuario del pedido que crea el
   circuito (la negociación de `/_blazor` y el websocket, que también pasan por el middleware)
   y `AdminSessionState.InicializarAsync` lo lee y queda autenticado (`Origen = AppHub`). Al
   recargar, el pedido vuelve a traer el token y entra solo. *(Corrige el borrador, que decía
   `HttpContext.Items`: Items no llega al circuito.)*
4. **Un token inválido nunca abre sesión.** Se registra una advertencia sin el contenido del
   token, y se muestra el login propio con el aviso.
5. **El login propio no sirve para la sesión de AppHub** y la sesión de AppHub no muestra
   «Salir» (se sale desde AppHub).

## 5. Reglas (del contrato §3–§6, aplicadas)

- Config: `AppHub:Aplicacion` = `datapipeline`; `AppHub:ClaveCompartida` vacía = autónomo;
  mínimo 32 caracteres si se usa; `AppHub:ToleranciaSegundos` (30, entre 0 y 300). Si hay
  clave y falta `Aplicacion`, **no arranca**.
- Token: JWT compacto de tres partes, `alg=HS256` (cualquier otro, incluido `none`, se
  rechaza), firma comparada en tiempo constante, `iss=AppHub`, `aud=AppHub:Aplicacion`,
  `exp` no vencido, `iat` no futuro (con tolerancia), `email` con forma de correo.
- Un solo encabezado: si viene repetido se rechaza.
- Ni el token ni la clave aparecen en el log.
- Un navegador que falsifique el encabezado **sin pasar por AppHub** no abre sesión (la
  firma no coincide).

## 6. Modelo de datos

Ninguno. Sin tablas, sin migraciones. Solo la sección `AppHub` en `appsettings.json` y la
clave real en la config del servidor (no en el repo).

## 7. Archivos previstos

| Archivo | Cambio |
|---|---|
| `DataPipeline.Admin/Services/TokenAppHub.cs` | Nuevo. Opciones + validación del token (portado del Conciliador, mismos motivos de rechazo). |
| `DataPipeline.Admin/Services/IdentidadAppHub.cs` | Nuevo. Middleware: valida y deja la identidad (o la marca de rechazada) como claims en `HttpContext.User`. |
| `DataPipeline.Admin/Services/AdminSessionState.cs` | `Origen` (Local/AppHub), `AppHubRechazada`; `InicializarAsync()` lee la identidad del usuario del circuito. |
| `DataPipeline.Admin/Components/Layout/MainLayout.razor` | Toma la identidad al crear el circuito; oculta «Salir» si `Origen = AppHub`; `login` relativo. |
| `DataPipeline.Admin/Components/Pages/Login.razor` | Aviso «AppHub no pudo confirmar tu identidad»; toma la identidad también acá; rutas relativas. |
| `DataPipeline.Admin/Components/App.razor` | `<base href>` con el `PathBase` de la config (no del host del pedido). |
| `Components/Pages/{Home,Envios/*,Pipelines/*}.razor` | Todas las navegaciones y enlaces pasan de `/ruta` a `ruta` (relativos a la base): con `PathBase` una ruta con «/» inicial saca a la persona del prefijo. Sin `PathBase` se comportan igual. |
| `DataPipeline.Admin/Program.cs` | Opciones, middleware, `PathBase`, `ForzarHttps`. |
| `DataPipeline.Admin/appsettings.json` | Sección `AppHub` (clave vacía). |
| `tests/DataPipeline.Tests/IdentidadAppHubTests.cs` | 36 pruebas: validador, configuración, middleware y sesión (checklist del contrato). |
| `docs/Despliegue-AppHub.md` | Alta en AppHub (`Apps[]`), clave compartida, `PathBase`, `Host`. |

## 8. Orden de implementación

1. `TokenAppHub` + pruebas (ítem 1).
2. `IdentidadAppHub` + `AdminSessionState` (ítems 2–3).
3. `MainLayout` / `Login` (ítem 3).
4. `Program.cs`: `PathBase`, https, config (ítem 4).
5. Guía de despliegue y verificación en navegador con un AppHub simulado (ítem 4).

## 9. Verificación

**Probado** (suite completa: 292 pruebas, 0 fallas) y **en navegador** con el Admin real, `PathBase=datapipeline`
y un proxy de prueba que agrega el encabezado firmado a cada pedido, websocket incluido, como haría AppHub:
token correcto → entra solo, con el correo y sin «Salir», interactivo, navegando bajo `/datapipeline/`; firma mala
→ login propio con el aviso; sin encabezado → login normal sin aviso; `/login` con identidad válida → inicio.

**No probado: el AppHub real publicado.** Dos supuestos que hay que confirmar con él:
1. **Que agregue `X-AppHub-Identidad` también a la negociación y al websocket de `/_blazor`**, no solo a los pedidos de
   página. Si no, el circuito no recibe la identidad y la persona cae en el login propio.
2. **Que conserve el `Host` original (o mande `X-Forwarded-Host`)**: Blazor arma las redirecciones con el host del
   pedido, y con el host interno la persona saldría del portal.

## Flujograma

![Flujograma](00-flujograma.png)

```mermaid
flowchart TD
    A([Pedido HTTP al Admin]) --> B{AppHub:ClaveCompartida<br/>configurada?}
    B -- No --> L[Modo autónomo:<br/>login dpAdmin, como hoy]
    B -- Sí --> C{Trae X-AppHub-Identidad?}
    C -- No --> L
    C -- "Sí, repetido" --> R
    C -- "Sí, uno solo" --> V{Valida: 3 partes, alg HS256,<br/>firma, iss, aud, exp, iat, correo}
    V -- "Falla (formato, algoritmo, firma,<br/>emisor, destinatario, vencido,<br/>futuro, correo)" --> R[Advertencia en el log SIN token<br/>User = claim «rechazada»]
    R --> LA[Login propio con aviso<br/>«AppHub no pudo confirmar tu identidad»]
    V -- Todo bien --> I[HttpContext.User = claims<br/>correo + nombre]
    I --> K["Se crea el circuito Blazor<br/>(toma el User del pedido)"]
    K --> S[AdminSessionState.InicializarAsync<br/>Origen = AppHub, administrador]
    S --> U[Log: correo que entró]
    U --> P[Pantallas del Admin<br/>cabecera con el correo, sin «Salir»]
    P --> Q{Qué hace la persona}
    Q -- "Recarga la página" --> A
    Q -- "Sale desde AppHub" --> X([Fin: el próximo pedido ya no trae token])
    Q -- "Token vence con el circuito abierto" --> M[Nada: el token vale solo al crear el circuito;<br/>la sesión sigue hasta cerrar o recargar]
    M --> P
    L --> LL{Contraseña correcta?}
    LA --> LL
    LL -- Sí --> PL[Sesión local Origen = Local<br/>con «Salir»]
    LL -- No --> LA2[Contraseña incorrecta]
    LA2 --> LL
```

## Maquetas

El Admin no gana pantallas ni campos. Lo único visible que cambia:

- `01-login-aviso.png` — `/login` con el aviso «AppHub no pudo confirmar tu identidad».
- `02-layout-apphub.png` — cabecera con el correo de quien entró por AppHub y **sin** «Salir».
