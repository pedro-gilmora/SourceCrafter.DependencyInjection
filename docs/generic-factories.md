# Generic factories — guia de diseño

> Decisiones cerradas en §2, §3, §4 y §6. El resto sigue abierto.

## 1. Que es

Una factory cuyo metodo declara parametros de tipo propios, de modo que un solo
registro sirva a toda una familia de tipos construidos en lugar de uno concreto.

```csharp
[ServiceProvider]
[Transient(source: nameof(_CreateLogger))]
public partial class Container
{
	private static ILogger<T> _CreateLogger<T>() where T : class => new Logger<T>();
}
```

## 2. Specs confirmadas

1. Todo parametro de tipo debe llevar al menos una restriccion: tipo base,
   interfaz, `class` o `struct`. Un `T` sin restricciones se rechaza.
2. El metodo es el unico responsable de construir la dependencia. El generador
   no infiere constructores para el tipo construido.

## 3. Solo transient — decidido

El argumento es el almacenamiento, no la semantica. Un cacheado necesita **un
campo por tipo construido**, y ese conjunto no se conoce registrando la factory:
se descubre en los sitios de consumo.

Con un unico registro genérico, `ILogger<Foo>` e `ILogger<Bar>` son dos servicios
distintos que exigen dos campos distintos. El coste crece con la familia y **no
se ve en el sitio de registro**, que es justo lo que lo hace mal negocio: se
escribe un atributo y se pagan N campos que nadie cuenta.

**Decidido:** `[Singleton]`/`[Scoped]` sobre factory generica se rechazan
(`SCDI22`). Si un tipo construido concreto necesita cache, se registra por
separado con su lifetime: ahi el campo es uno, explicito y visible.

Y como no hay cache, **no hay nada que guardar**: no se emite campo ni propiedad
de respaldo. La resolucion es una llamada directa a la factory.

## 4. Como se descubren los tipos construidos

Esta es la decision central, y condiciona todo lo demas.

### Opcion A — cierre por consumo (recomendada para empezar)

El generador recorre el grafo y recoge cada `ILogger<X>` que aparezca como
parametro de un servicio registrado. Esos son los tipos construidos.

```csharp
public sealed class OrderService(ILogger<OrderService> log);   // -> ILogger<OrderService>
public sealed class Cart(ILogger<Cart> log);                   // -> ILogger<Cart>
```

- Conjunto **cerrado y conocido en compilacion**. Habilita cacheados.
- No sirve para resolucion dinamica por parte del usuario.

### Opcion B — cierre por registro explicito

```csharp
[Transient(source: nameof(_CreateLogger), constructs: [typeof(OrderService), typeof(Cart)])]
```

- Explicito y predecible; el usuario controla el conjunto.
- Verboso y se desincroniza al añadir consumidores.

### Opcion C — miembro generico abierto

Emitir `public ILogger<T> GetLogger<T>() where T : class => _CreateLogger<T>();`

- Soporta cualquier `T`, incluso no visto en compilacion.
- Incompatible con cacheado y con el resto de la forma generada.

**Decidido:** opcion A. C puede añadirse despues como opcion explicita.

## 5. Resolucion y nombres de miembro

Sin cache no hace falta miembro publico de respaldo: cada consumo invoca la
factory en el punto donde se necesita, igual que cualquier transient.

```csharp
// consumo: OrderService(ILogger<OrderService>)
new OrderService(_CreateLogger<OrderService>())
```

Si mas adelante se expone un miembro por tipo construido, habra que confirmar las
colisiones entre `Ns1.Foo` y `Ns2.Foo`; el contador de `MemberNamingTests`
deberia bastar.

## 6. Emparejado y ambiguedad

Al resolver `ILogger<Foo>`, si varias factories genericas pueden producirlo, gana
la **mas especifica**; si empatan, es error.

```csharp
private static IRepo<T> _Generic<T>() where T : class;
private static IRepo<T> _Entities<T>() where T : class, IEntity;   // mas especifica
// IRepo<Customer> con Customer : IEntity  -> _Entities
```

Un registro concreto de `IRepo<Customer>` **siempre** gana a cualquier generico.

**Decidido:** los empates se rechazan y se desambiguan **con llave (key)**. No se
inventa un orden de precedencia — ni declaracion, ni ensamblado — porque eso
haria depender el servicio elegido de algo que no se lee en el sitio de registro.
La key ya es el mecanismo de desambiguacion del resto del generador, asi que no
añade vocabulario nuevo.

## 7. Restricciones

Las restricciones son parte del emparejado, no solo validacion. Si `T` no las
satisface, esa factory no es candidata (no es un error de por si: puede haber otra).

Si **ninguna** candidata acepta el tipo, error con la restriccion incumplida.

## 8. Diagnosticos propuestos

`SCDI17` y `SCDI18` ya estaban ocupados, asi que la familia arranca en `SCDI19`.

| Id | Caso | Estado |
|---|---|---|
| SCDI19 | Parametro de tipo sin restricciones (spec 1) | implementado |
| SCDI20 | Ninguna factory generica acepta el tipo pedido | declarado |
| SCDI21 | Dos factories genericas empatan; desambiguar con key | declarado |
| SCDI22 | Lifetime cacheado sobre factory generica | implementado |

`new()` no cuenta como restriccion para `SCDI19`: exige un constructor sin
parametros pero no acota el conjunto de tipos admisibles, asi que no sirve como
criterio de emparejado.

## 9. Interaccion con lo que ya existe

- **Async**: `Task<ILogger<T>>` deberia funcionar sin cambios; `SCDI16` ya cubre
  el mismatch de tipo declarado. **A verificar.**
- **Keyed**: una key sobre factory generica aplica a toda la familia, no a un
  tipo construido. **Decision abierta:** ¿se permite?
- **Multiple/interceptores**: ¿`IEnumerable<ILogger<Foo>>` recoge las genericas?
  Probablemente si, pero cambia el emparejado. **A verificar.**
- **Disposables**: sin cambios; el transient desechable ya se rastrea por miembro.

## 10. Orden sugerido

1. ~~Parsear factories genericas + `SCDI19` + `SCDI22`.~~ **hecho**
2. Cierre por consumo (§4-A): recoger los `ILogger<X>` del grafo.
3. Emparejado con restricciones + `SCDI20`.
4. Ambiguedad + `SCDI21` (desambiguacion por key).
5. Revisar §9.
6. Reevaluar cacheados a la luz de lo aprendido.

## 11. Lo que no se ha verificado

- ~~Si el parser tolera hoy un `IMethodSymbol` generico.~~ Lo tolera: el registro
  se parsea y los diagnosticos se emiten sobre el.
- Si el naming actual soporta argumentos de tipo en el nombre.
- Si el grafo permite descubrir consumidores antes de resolver (§4-A depende
  de ello y podria obligar a una pasada extra).
