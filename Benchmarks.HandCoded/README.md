# Benchmarks.HandCoded

Banco que mide **la mejor version escrita a mano de un contenedor de inyeccion de dependencias**
contra los generadores del ecosistema: Jab, Pure.DI, CircleDI y SourceCrafter.

No evalua generadores de codigo. Evalua **cuanto techo queda** por encima de lo que hoy emite cada
uno, midiendo contra una implementacion escrita a mano que replica su semantica.

## Como se ejecuta

```powershell
dotnet build -c Release
.\bin\Release\net10.0\Benchmarks.HandCoded.exe --check          # solo verificacion semantica
.\bin\Release\net10.0\Benchmarks.HandCoded.exe --fast --filter "*Control*"
.\bin\Release\net10.0\Benchmarks.HandCoded.exe --filter "*"     # publicacion (~57 min, 111 casos)
```

La verificacion semantica corre **siempre** antes de medir y aborta la corrida si falla.

## Protocolo de lectura

Sin excepciones, y en este orden:

1. **Mirar primero `ControlBenchmark`.** Son seis metodos que ejecutan el mismo codigo. Si no
   quedan agrupados, ninguna otra tabla de esa corrida es publicable.
2. **Una diferencia con `RatioSD` comparable al `Ratio` no es una diferencia.**
3. **La columna `Allocated` es fiable siempre**; es un recuento, no un tiempo. Cuando el tiempo es
   ruidoso y la memoria no, se reporta la memoria.
4. **Sospechar de cualquier cifra por debajo de 0,1 ns.** Suele significar que el JIT elimino el
   escenario, no que el escenario sea rapido.

El suelo de discriminacion del instrumento en esta maquina es de **~9%**. Por debajo de ~3 ns el
banco no distingue: dos filas con codigo identico llegaron a separarse un 21%.

## Resultados

Medido en un i9-14900HX con afinidad fijada a los P-cores (mascara `0x5555`).

### Donde se gana: el ambito vacio

| | Tiempo | Asignado |
|---|---:|---:|
| **Hand-coded lazy** | **3,18 ns** | **0 B** |
| CircleDI | 6,67 ns | 48 B |
| SourceCrafter | 9,63 ns | 56 B |
| Pure.DI | 24,26 ns | 152 B |
| Jab | 25,93 ns | 56 B |

**2,1x mas rapido que el mejor rival y sin tocar el monton.** El cero no es un error de medida: el
analisis de escape de .NET 10 coloca el ambito en la pila, y solo puede hacerlo porque el ambito es
pequeño (cuatro referencias), **no tiene candado propio** y **no tiene lista de desecho** -- desecha
leyendo los campos que el compilador ya sabe que son desechables y saltando los nulos.

Es el escenario mayoritario en un servidor: una peticion servida desde cache, un chequeo de salud o
una ruta estatica abren su ambito y no resuelven nada scoped.

### Donde se empata, y no se puede hacer otra cosa

**Camino caliente de un singleton.** Las seis implementaciones caen entre 0,28 y 0,66 ns con
`RatioSD` de 0,67 a 0,84. La tabla **no discrimina**, y esa es la conclusion: leer un campo
publicado tiene un suelo duro que cualquiera alcanza. No queda nada que optimizar por ahi.

**Ciclo completo de ambito.** Hand-coded eager 9,75 ns frente a CircleDI 10,56 ns: un 8%, dentro del
ruido. Contra el resto si hay distancia: SourceCrafter 2,6x, Pure.DI 3,6x, Jab 4,1x.

**Transitorio.** Las cinco implementaciones asignan **24 B exactos**. Los tiempos (2,1-2,7 ns) estan
por debajo del suelo de discriminacion.

### Lo que la variante perezosa no puede ganar

El ciclo completo de ambito con semantica perezosa cuesta 20,6 ns frente a los 10,56 de CircleDI.
No es un defecto de implementacion: **la primera resolucion de cada ambito cae siempre en el camino
frio**, con su candado. CircleDI no lo paga porque construye en el constructor. Es el precio de la
pereza, y se cobra una vez por ambito.

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

Publicar sin candado usando `Interlocked.CompareExchange` obliga a **construir antes de poder
publicar**. Medido por la sonda de `--check`:

| Hilos | Instancias descartadas |
|---:|---:|
| 2 | 11,3% |
| 4 | 42,2% |
| 8 | 82,3% |

Para un servicio sin recursos eso solo es basura. Para un `IDisposable` es una **fuga**: la instancia
perdedora nunca se publico, el contenedor no la conoce y nadie la va a desechar. Por eso las celdas
**sin candado + desechable no son una alternativa mas rapida: son incorrectas**. Se miden -- omitirlas
dejaria un hueco que el lector rellenaria suponiendo -- pero no se publican como validas.

## Dos trampas que este banco cazo

Ambas producian resultados que *parecian buenos*, que es lo que las hace peligrosas.

**1. El escenario que no existia.** La primera version del ambito vacio midio `0,0097 ns con 0 B`.
Una centesima de nanosegundo es la trescentesima parte de un ciclo de reloj. El JIT vio que el ambito
no salia del metodo y que desecharlo no tenia efecto observable, y **elimino el escenario entero**.
La fila salia imbatible y hasta la columna de asignacion, que suele ser la de fiar, marcaba cero.
Corregido obligando a cada metodo a devolver el ambito.

**2. La comparacion desigual.** Los contenedores de la matriz implementan nueve servicios; los de los
rivales, tres. Su ambito pesaba 144 B contra 48 B y perdia -- pero perdia por llevar tres veces mas
servicios, no por estar peor escrito. Corregido con `LeanLazyContainer`, que registra exactamente los
tres servicios de los rivales.

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
5. **No usar CAS para publicar servicios.** El descarte llega al 82%.
