# Benchmarks.HandCoded

Banco que mide **la mejor version escrita a mano de un contenedor de inyeccion de dependencias**
contra los generadores del ecosistema: Jab, Pure.DI, CircleDI y SourceCrafter.

No evalua generadores de codigo. Evalua **cuanto techo queda** por encima de lo que hoy emite cada
uno, midiendo contra una implementacion escrita a mano que replica su semantica.

## Como se ejecuta

Las tablas se seleccionan con una **mascara de bits**, no con comodines de cadena:

```powershell
dotnet build -c Release
$bench = ".\bin\Release\net10.0\Benchmarks.HandCoded.exe"

& $bench --list              # tabla de valores; es tambien lo que sale sin argumentos
& $bench --check             # solo verificacion semantica, no mide nada
& $bench 3                   # control + publicacion
& $bench 3841                # Report: control + los cuatro head-to-head
& $bench 1 --fast            # iterar rapido (los tiempos NO son publicables)
& $bench Control,HeadToHeadScope   # tambien acepta nombres
& $bench 4095                # All (~57 min, 111 casos)
```

| Valor | Tabla | | Valor | Tabla |
|---:|---|---|---:|---|
| 1 | `Control` | | 128 | `MatrixTransientDisposable` |
| 2 | `Publication` | | 256 | `HeadToHeadSingleton` |
| 4 | `EagerVsLazy` | | 512 | `HeadToHeadScope` |
| 8 | `ScopeLifecycle` | | 1024 | `HeadToHeadCreation` |
| 16 | `MatrixSingleton` | | 2048 | `HeadToHeadTransient` |
| 32 | `MatrixScoped` | | 240 | `Matrix` (las cuatro) |
| 64 | `MatrixTransientPlain` | | 3840 | `HeadToHead` (las cuatro) |

**El control es el bit 0 a proposito.** Es el unico valor impar util, asi que la costumbre correcta
-- sumarle 1 a la mascara que te interese -- es tambien la mas corta de escribir. Si la mascara no
lo incluye, el arnes avisa por consola de que las cifras no son publicables.

**Sin argumentos no se mide nada**: se imprime la tabla y se sale. Correr las doce tablas cuesta casi
una hora y no debe ser lo que pasa por escribir `dotnet run` sin pensar.

La verificacion semantica corre **siempre** antes de medir y aborta la corrida si falla.

## Escrituras dentro del candado

Los contenedores `*Locked*` son los titulares del eje "con candado", asi que **no usan `Volatile`
ni `Interlocked` en ninguna parte**. Mezclarlos confundiria los dos ejes que la matriz separa: una
celda que dijera "con candado" y por dentro publicase con un CAS no mediria el candado, mediria una
mezcla. La variante sin candado vive entera en `LockFreeContainer`.

- **Publicar** es una asignacion normal dentro del candado. Salir de un `lock` ya es una barrera de
  liberacion: todo lo escrito dentro, constructor del servicio incluido, es visible para quien
  despues adquiera ese mismo candado. Un `Volatile.Write` ahi no añade garantia alguna.
- **Leer** en el camino caliente es una lectura normal. Esto si tiene letra pequeña: el lector rapido
  no toma el candado, asi que no hereda su barrera. Lo que lo salva es que el runtime de .NET da
  semantica de liberacion a *toda* escritura de referencia, no solo a las volatiles, asi que la
  instancia nunca se publica a medio construir. Lo que se pierde es la barrera de compilador. Aqui
  da igual porque cada resolucion vuelve a entrar al captador, pero el mismo patron dentro de un
  bucle de espera giraria para siempre. **ECMA-335 no lo garantiza; el runtime, si.**

### La segunda prueba de nulo no es ceremonia

En el codigo va escrita como `??=`, que compila exactamente a "lee, si es null construye y asigna".
Elegir esa forma tiene un motivo concreto: deja el cuerpo del candado en una linea y hace que la
version rota se distinga por **un solo caracter** (`= new(...)` en lugar de `??= new(...)`).

Es la parte del patron que mas invita a "simplificarse", porque parecen dos pruebas de nulo seguidas
y da la sensacion de que la de dentro sobra. Medido con la sonda de `--check`, 20.000 rondas:

| Estrategia (8 hilos) | Construye de mas | Identidad rota |
|---|---:|---:|
| `lock` con `??=` | 0,0% | 0 |
| `CompareExchange` | 80,7% | 0 |
| `Exchange` | 80,2% | 8.508 |
| **`lock` con `=`** | **168,8%** | **11.630** |

**Quitarla es peor que no tener candado.** El candado serializa a los hilos y luego los deja pisarse
en fila: cada uno entra, construye y sobrescribe lo que dejo el anterior. A 8 hilos construye casi el
triple de lo necesario y en el 58% de las rondas dos llamadores acaban con instancias distintas.

## Singletons estaticos

Los singletons de `LockedContainer` son campos `static` con candado `static`, que es la forma que
emite SourceCrafter. Los campos de ambito, por contraste, son de instancia: ahi `static` no seria una
forma discutible sino un error, porque un scoped compartido entre ambitos deja de ser scoped.

**Medido, `static` no cambia el camino caliente.** El contenedor con candado (campos estaticos) da
0,595 ns en la celda sync y el lock-free (campos de instancia) 0,552 ns, con `RatioSD` de 0,06 a 0,22:
indistinguibles. La teoria de que el JIT hornea la direccion del estatico y se ahorra desreferenciar
`this` no se materializa en una ganancia observable, porque el captador es de instancia igualmente y
todo se reduce a un `mov` en ambos casos.

**Lo que si cambia es la semantica, y no es gratis.** Un singleton estatico no pertenece a ningun
contenedor, asi que "desecharlo" no es una operacion bien definida. Aqui se resuelve desechandolo y
poniendo el campo a `null` bajo el candado, de modo que el siguiente contenedor lo reconstruye. Eso
mantiene medible el eje de disposability, pero deja tres consecuencias fijadas por escrito en
`--check`:

- el singleton se comparte entre contenedores distintos;
- **desechar un contenedor desecha el singleton de todos los demas del proceso**;
- el siguiente en pedirlo recibe uno nuevo, no el desechado.

Es correcto en el caso real -- un proceso tiene un contenedor raiz y lo dispone al terminar -- y es
una bomba en cualquier escenario que cree varios a la vez. Esta medido en vez de comentado justamente
por eso. Es tambien la causa raiz del `sc-disposable-swap` que sigue abierto en el generador.

## Protocolo de lectura

Sin excepciones, y en este orden:

1. **Mirar primero `ControlBenchmark`.** Son metodos identicos medidos a **tres magnitudes**. Si las
   tres filas del grupo correspondiente a la tabla que quieres leer no quedan agrupadas, esa tabla no
   es publicable.
2. **Una diferencia con `RatioSD` comparable al `Ratio` no es una diferencia.**
3. **La columna `Allocated` es fiable siempre**; es un recuento, no un tiempo. Cuando el tiempo es
   ruidoso y la memoria no, se reporta la memoria.
4. **Sospechar de cualquier cifra por debajo de 0,1 ns.** Suele significar que el JIT elimino el
   escenario, no que el escenario sea rapido.
5. **Sospechar tambien de cualquier escenario que salga mas barato que uno estrictamente contenido en
   el.** Crear un contenedor eager cuesta 8,05 ns / 112 B; crearlo *y ademas* resolver un servicio
   sale 1,63 ns / 24 B. No es magia: al leer un solo campo, el analisis de escape elimina el
   contenedor entero. Esa fila no mide lo que dice medir.

### Por que el control mide tres magnitudes

Porque medir una sola lo hacia inutil justo donde mas falta hacia. La version anterior media el grafo
profundo (~35 ns), pasaba con holgura, y con ese visto bueno se publicaban tablas de 0,5 ns cuyo
`RatioSD` iba de 0,67 a 0,84. **Un control a 35 ns no dice nada sobre el ruido a 0,5 ns.**

Y el resultado no es el que dicta la intuicion:

| Magnitud | Dispersion entre codigo identico | Barra de error tipica |
|---|---:|---:|
| ~0,56 ns (lectura de campo, sin asignar) | **0,3%** | ±5,5% |
| ~1,7 ns (una asignacion de 24 B) | **10,1%** | ±1,7% |
| ~35 ns (grafo de 424 B) | **3,1%** | ±2,5% |

El ruido **no crece ni decrece con la magnitud**: el peor grupo es el del medio. Lo que lo dispara es
*asignar poco*, donde el estado del asignador pesa mas que el trabajo medido. Extrapolar el suelo de
una magnitud a otra es exactamente el error que se cometio antes.

Un matiz que hay que leer con cuidado: en el grupo de 0,56 ns las tres medias caen dentro del 0,3%
**pero cada una trae una barra de ±5,5%**. Coincidir tan fino con ese error es suerte, no precision.
El suelo real a esa magnitud es la barra, no la dispersion: **por debajo de un 6% a 0,5 ns no se
afirma nada.**

## Resultados

Medido en un i9-14900HX con afinidad fijada a los P-cores (mascara `0x5555`).

### Las tablas van partidas: perezosos por un lado, CircleDI por otro

**CircleDI no usa ni un solo primitivo de sincronizacion.** Construye todo en el constructor y lo deja
en campos de solo lectura: es seguro entre hilos por inmutabilidad. Jab, Pure.DI y SourceCrafter son
perezosos y pagan por contrato una lectura volatil, un salto y, en la primera resolucion, exclusion
mutua.

Meterlos en una sola tabla con un solo baseline presenta como diferencia de calidad lo que es una
diferencia de contrato. Por eso cada head-to-head tiene dos grupos con su propio baseline: **sin
candados** (hand-coded eager frente a CircleDI) y **perezosos** (hand-coded lazy frente a los tres).

El efecto de separarlos es inmediato: contra su igual, **CircleDI empata en las cuatro tablas** --
0,98x en ambito vacio, 0,98x en ciclo completo, 1,02x al crear, 1,01x al crear y resolver. Mezclado
con los perezosos parecia ganar unas y perder otras, y ninguna de las dos lecturas era un juicio sobre
su codigo.

### Donde se gana: el ambito vacio

| perezosos | Tiempo | Asignado |
|---|---:|---:|
| **Hand-coded lazy** | **3,03 ns** | **0 B** |
| SourceCrafter | 9,30 ns | 56 B |
| Pure.DI | 16,66 ns | 152 B |
| Jab | 21,76 ns | 56 B |

**3,1x mas rapido que el mejor perezoso y sin tocar el monton.** El cero no es un error de medida: el
analisis de escape de .NET 10 coloca el ambito en la pila, y solo puede hacerlo porque el ambito es
pequeño (cuatro referencias), **no tiene candado propio** y **no tiene lista de desecho** -- desecha
leyendo los campos que el compilador ya sabe que son desechables y saltando los nulos.

Es el escenario mayoritario en un servidor: una peticion servida desde cache, un chequeo de salud o
una ruta estatica abren su ambito y no resuelven nada scoped.

Detalle que merece atencion: el ambito **perezoso** (3,03 ns / 0 B) sale mas barato que el **eager**
(5,31 ns / 48 B). Ser eager obliga a que el ambito contenga instancias construidas, y eso lo saca de
la pila. La pereza no siempre cuesta.

### Donde se empata, y no se puede hacer otra cosa

**Camino caliente de un singleton.** Las seis implementaciones caen entre 0,507 y 0,590 ns. La
separacion maxima es del 16%, pero las barras de error son de ±5-10% y el control a esa magnitud dice
que por debajo del 6% no se afirma nada; el unico valor fuera de banda es CircleDI (0,92x) y esta
justo en el limite. **La tabla no discrimina, y esa es la conclusion:** leer un campo publicado tiene
un suelo duro que cualquiera alcanza. No queda nada que optimizar por ahi.

**Ciclo completo de ambito, sin candados.** Hand-coded eager 7,17 ns frente a CircleDI 6,97 ns, con
72 B exactos en las dos filas. Empate.

**Transitorio.** Las cinco implementaciones asignan **24 B exactos**.

### Lo que la variante perezosa no puede ganar

El ciclo completo de ambito con semantica perezosa cuesta 15,88 ns frente a los 6,97 de CircleDI. No
es un defecto de implementacion: **la primera resolucion de cada ambito cae siempre en el camino
frio**, con su candado. CircleDI no lo paga porque construye en el constructor. Es el precio de la
pereza, y se cobra una vez por ambito. Contra sus iguales, el perezoso escrito a mano gana: 1,26x a
SourceCrafter, 1,58x a Pure.DI, 2,15x a Jab.

## Como publicar un singleton: cinco estrategias

El eje aqui **no es la velocidad**, y la tabla lo demuestra desde los dos lados.

**Camino caliente** (la instancia ya esta publicada): las cinco estrategias caen entre 0,479 y
0,587 ns, dentro de la banda del control. Incluso la **lectura no volatil** (0,555 ns) sale igual que
la volatil. En x86 `Volatile.Read` es gratis: **no hay nada que ganar evitandolo, y si hay un modelo
de memoria que romper.**

**Publicacion** (la primera vez, una sola vez por servicio en toda la vida del proceso):

| Estrategia | Publicar | Construye de mas | Identidad rota |
|---|---:|---:|---:|
| `lock(this)` | 12,34 ns | 0,0% | 0 |
| `lock` estatico | 11,43 ns | 0,0% | 0 |
| `CompareExchange` | 6,36 ns | 14,5-38,2% | 0 |
| `Exchange` | **5,30 ns** | 13,4-57,1% | **13-32% de las rondas** |

**La opcion mas rapida es la unica que esta rota.** `Exchange` escribe siempre sin mirar lo que habia:
el hilo A publica la instancia 1 y se la lleva, el hilo B publica la 2 encima, y todo el que llegue
despues recibe la 2. Dos llamadores tienen dos "singletons" distintos vivos a la vez. La sonda de
`--check` lo cuenta: hasta 6.373 de 20.000 rondas.

`CompareExchange` es cualitativamente mejor -- **cero violaciones de identidad**, porque solo escribe
si el campo sigue nulo y el perdedor devuelve lo que encontro -- pero descarta hasta un 38% de lo que
construye. Si el servicio es `IDisposable`, a la instancia perdedora no la desecha nadie.

Y lo que se compra con el riesgo son **6 nanosegundos que se cobran una vez por servicio**. Un proceso
con cincuenta servicios ahorra 300 ns en toda su vida.

Entre los dos candados no hay diferencia medible (12,34 vs 11,43 ns, con barras de ±1,01 y ±0,13). La
razon para preferir uno u otro no aparece en este banco, que es monohilo: un `lock(this)` en un ambito
no estorba entre peticiones porque cada ambito escribe en sus propios campos, mientras que un candado
estatico serializa el proceso entero. Para scoped, `lock(this)`. Para singleton, da igual.

## La matriz atomica

Las cuatro tablas `Matrix*` cubren cada combinacion de *locking x lifetime x async-kind x
disposability*. Hallazgos:

- **La disposability no mueve el camino caliente** en ninguna de las 18 celdas. Desechar ocurre al
  final de la vida del objeto, no al resolverlo.
- **Una fabrica `Task` asigna 96 B; una `ValueTask`, 24 B.** Cuatro veces mas, y es un recuento, asi
  que es reproducible.
- **El camino rapido asincrono funciona:** un servicio cacheado ya publicado se devuelve en 0,76-0,84
  ns **sin asignar nada**, frente a 0,57 ns del sincrono. No se crea maquina de estados.
- **Con candado y sin candado son indistinguibles en el camino caliente**, porque ejecutan el mismo
  codigo. Las seis celdas de transitorio no desechable son identicas por construccion y sirven de
  tercer grupo de control.

### Las celdas incorrectas

Publicar sin candado obliga a **construir antes de poder publicar**, y hay dos formas de hacerlo mal
que no son igual de malas. La sonda de `--check` las separa (20.000 rondas por celda):

| Hilos | `CompareExchange` descarta | `Exchange` descarta | `Exchange` rompe identidad |
|---:|---:|---:|---:|
| 2 | 14,5% | 13,4% | 2.688 rondas (13,4%) |
| 4 | 38,2% | 34,7% | 4.993 rondas (25,0%) |
| 8 | 35,0% | 57,1% | 6.373 rondas (31,9%) |

Con candado: **0,0% de descarte y 0 violaciones en las nueve celdas.**

`CompareExchange` **converge**: la columna de identidad es cero en las tres filas, porque solo escribe
si el campo sigue nulo y el perdedor devuelve lo que encontro. Su defecto es la fuga -- la instancia
descartada nunca se publico, el contenedor no la conoce y si es `IDisposable` nadie la desecha.

`Exchange` es **cualitativamente peor**: escribe siempre, pisando lo que hubiera. Hasta un tercio de
las rondas acaban con dos llamadores sosteniendo instancias distintas. Eso ya no es un singleton con
una fuga, es que no es un singleton.

Por eso las celdas **sin candado + desechable no son una alternativa mas rapida: son incorrectas**. Se
miden -- omitirlas dejaria un hueco que el lector rellenaria suponiendo -- pero no se publican como
validas.

## Tres trampas que este banco cazo

Las tres producian resultados que *parecian buenos*, que es lo que las hace peligrosas.

**1. El escenario que no existia.** La primera version del ambito vacio midio `0,0097 ns con 0 B`.
Una centesima de nanosegundo es la trescentesima parte de un ciclo de reloj. El JIT vio que el ambito
no salia del metodo y que desecharlo no tenia efecto observable, y **elimino el escenario entero**.
La fila salia imbatible y hasta la columna de asignacion, que suele ser la de fiar, marcaba cero.
Corregido obligando a cada metodo a devolver el ambito.

**2. La comparacion desigual.** Los contenedores de la matriz implementan nueve servicios; los de los
rivales, tres. Su ambito pesaba 144 B contra 48 B y perdia -- pero perdia por llevar tres veces mas
servicios, no por estar peor escrito. Corregido con `LeanLazyContainer`, que registra exactamente los
tres servicios de los rivales.

**3. El control que validaba la magnitud equivocada.** Seis metodos identicos a ~35 ns pasaban con un
3% de dispersion, y ese aprobado se usaba para publicar tablas de 0,5 ns cuyo `RatioSD` llegaba a
0,84. El control daba **confianza falsa**: media una escala y avalaba otra. Corregido midiendo tres
magnitudes, y el resultado desmonta la intuicion de la que venia el error -- el grupo mas ruidoso no
es el mas rapido ni el mas lento, **es el del medio** (10,1% de dispersion a 1,7 ns, frente al 3,1% a
35 ns). El ruido no es una funcion de la magnitud.

**Todavia sin corregir:** en la tabla de creacion, las filas de "crear+resolver" de los contenedores
eager salen *mas baratas* que las de "crear" a secas (1,63 ns / 24 B frente a 8,05 ns / 112 B). Leer
un solo campo le basta al analisis de escape para eliminar el contenedor entero. Esas dos filas estan
publicadas con la advertencia puesta, no borradas, porque el patron -- un escenario mas barato que
otro estrictamente contenido en el -- es la firma de este fallo y conviene tenerla a la vista.

## Metodologia

La maquina de desarrollo es un i9-14900HX: 8 P-cores con SMT (logicos 0-15) y 16 E-cores (16-31).
Medido sobre seis metodos identicos con asignacion identica byte a byte:

| | Tiempo |
|---|---:|
| P-cores | 33,00 - 35,07 ns |
| E-cores | 72,69 - 73,94 ns |

**2,2x por el mismo codigo.** Sin fijar la afinidad, una tabla puede repartir sus filas entre los dos
modos y producir "diferencias" del doble que no existen. Fue la causa real de un informe que hubo que
retractar en este repositorio.

De ahi la mascara `0x5555`: un logico por P-core fisico, sin hermanos SMT, porque el proceso que
coordina la corrida puede caer en el hermano del nucleo que esta midiendo. Con `0xFFFF` quedaba un
metodo con StdDev de 7,10 ns; con `0x5555` ninguno pasa de 1,08 ns. Eso permite bajar `LaunchCount`
de 3 a 1: mas rapido **y** mas fiable.

**El banco no se puede paralelizar.** Medir entre 0,5 y 35 ns exige el nucleo, su cache y el ancho de
banda dedicados. Con 32 logicos, repartir escenarios *garantizaria* mandar trabajo a los E-cores, que
es el artefacto de arriba fabricado a proposito.

## Que deberia aprender el generador

1. **Desechar por campo, no por lista.** Es lo que separa un ambito de 48 B de uno que asigna una
   `List<T>` y su array en cuanto resuelve el primer servicio. El generador ya sabe en tiempo de
   compilacion cuales de sus servicios son desechables.
2. **Ningun candado por ambito.** Compartir el de la raiz basta: solo protege caminos frios, donde la
   contienda entre ambitos distintos es irrelevante porque cada uno escribe en sus propios campos.
3. **Un `DisposeAsync` con camino rapido que se pueda alinear.** Es lo que permite al JIT probar que
   el ambito no escapa y colocarlo en la pila.
4. **Preferir `ValueTask` a `Task` en las fabricas.** Cuatro veces menos asignacion.
5. **Publicar siempre con candado, nunca con `Interlocked`.** No es una preferencia de estilo: el
   `Exchange` entrega instancias distintas a llamadores distintos en hasta un tercio de las carreras,
   y el `CompareExchange`, que si respeta la identidad, fuga hasta un 38% de lo que construye. Lo que
   se compra a cambio son **6 ns que se cobran una vez por servicio en toda la vida del proceso**.
   Optimizar la publicacion es optimizar algo que pasa una vez.
6. **No evitar `Volatile.Read` por rendimiento.** En el camino caliente cuesta exactamente lo mismo
   que una lectura normal (0,555 ns frente a 0,479-0,587 ns de las demas estrategias, todas dentro de
   la banda del control). Se estaria rompiendo el modelo de memoria a cambio de nada.
7. **Decidir la pereza del candado por grafo.** Un contenedor cuyos servicios son todos eager no
   necesita candado y no deberia asignarlo; uno con algun servicio perezoso lo necesita siempre y
   hacerlo perezoso es sobrecoste puro. El generador sabe cual es cual en tiempo de compilacion.
