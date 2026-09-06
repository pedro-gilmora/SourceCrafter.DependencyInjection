namespace Benchmarks;

/// <summary>
/// Grafo de servicios compartido por <b>todos</b> los contenedores del banco de pruebas.
/// <para>
/// Que sea uno solo es deliberado: la version anterior duplicaba los tipos en un namespace
/// por libreria, con formas ligeramente distintas, de modo que las cifras no eran
/// comparables entre si. Aqui lo unico que cambia entre benchmarks es la declaracion del
/// contenedor.
/// </para>
/// </summary>
public interface ISettings;

public sealed class Settings : ISettings;

public interface IDatabase;

public sealed class Database(ISettings settings) : IDatabase
{
    public ISettings Settings { get; } = settings;
}

public interface ISession : IDisposable;

public sealed class Session(IDatabase database) : ISession
{
    public IDatabase Database { get; } = database;

    public void Dispose() { }
}

// ----- Grafo profundo para el escenario "Complex" -----
// Cuatro niveles, 15 nodos: la misma forma que usan los benchmarks de Pure.DI, para que
// el numero sea comparable con lo que publica la competencia.

public sealed class Leaf;

public sealed class Level3(Leaf first, Leaf second)
{
    public Leaf First { get; } = first;
    public Leaf Second { get; } = second;
}

public sealed class Level2(Level3 first, Level3 second)
{
    public Level3 First { get; } = first;
    public Level3 Second { get; } = second;
}

public sealed class Level1(Level2 first, Level2 second, IDatabase database)
{
    public Level2 First { get; } = first;
    public Level2 Second { get; } = second;
    public IDatabase Database { get; } = database;
}
