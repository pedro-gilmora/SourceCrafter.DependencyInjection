using System;
using System.Globalization;
using System.Text;

namespace SourceCrafter.DependencyInjection.Generation;

/// <summary>
/// Escritor de codigo con sangria aplicada al escribir: cada linea recibe la profundidad actual con su primer caracter
/// visible, asi que los fragmentos se escriben a profundidad 0 y se anidan sin reescribir el buffer (nada de Replace).
/// Las lineas en blanco no llevan espacios y los saltos se normalizan a '\n'.
/// <para>Modelo: <see cref="Line(string?)"/> empieza linea, <see cref="Append(string?)"/> continua la actual,
/// <see cref="Open"/>/<see cref="OpenInline"/> abren un bloque y <see cref="Close"/> lo cierra ya desindentado.</para>
/// </summary>
internal sealed class CodeBuilder(int depth = 0, int capacity = 1024)
{
    private const int IndentSize = 4;

    private readonly StringBuilder _sb = new(capacity);
    private int _depth = depth;
    private bool _lineStart = true;
    // ponytail: bit n = el bloque de profundidad n sigue en su linea de apertura (un salto los rompe todos); mas alla de 64 niveles se cierran multilinea.
    private ulong _inline;

    public int Depth => _depth;

    /// <summary>Truncar resincroniza el estado de inicio de linea.</summary>
    public int Length
    {
        get => _sb.Length;
        set { _sb.Length = value; Sync(); }
    }

    public char this[int index] => _sb[index];

    public CodeBuilder Indent() { _depth++; return this; }

    public CodeBuilder Unindent() { if (_depth > 0) _depth--; return this; }

    /// <summary>Continua la linea actual; los saltos internos empiezan lineas sangradas a la profundidad actual.</summary>
    public CodeBuilder Append(string? text) => text is null ? this : Append(text.AsSpan());

    public CodeBuilder Append(ReadOnlySpan<char> text)
    {
        while (true)
        {
            int nl = text.IndexOf('\n');

            if (nl < 0) { Write(text); return this; }

            var line = text.Slice(0, nl);
            if (!line.IsEmpty && line[line.Length - 1] == '\r') line = line.Slice(0, line.Length - 1);

            Write(line);
            NewLine();
            text = text.Slice(nl + 1);
        }
    }

    public CodeBuilder Append(char c)
    {
        if (c == '\n') NewLine();
        else if (c != '\r') Write([c]);

        return this;
    }

    public CodeBuilder Append<T>(T value) where T : ISpanFormattable
    {
        Span<char> buffer = stackalloc char[32];

        return value.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture)
            ? Append(buffer.Slice(0, written))
            : Append(value.ToString(null, CultureInfo.InvariantCulture));
    }

    /// <summary>Anida otro escritor: sus lineas se sangran a la profundidad actual, sin materializar su texto.</summary>
    public CodeBuilder Append(CodeBuilder other)
    {
        foreach (var chunk in other._sb.GetChunks()) Append(chunk.Span);
        return this;
    }

    /// <summary>Empieza una linea nueva (si la actual tiene algo visible; si solo tiene espacios se reutiliza a la profundidad actual) y escribe <paramref name="text"/>.</summary>
    public CodeBuilder Line(string? text = null)
    {
        if (!_lineStart)
        {
            int end = _sb.Length;
            while (end > 0 && _sb[end - 1] is ' ' or '\t') end--;

            if (end == 0 || _sb[end - 1] == '\n') { _sb.Length = end; _lineStart = true; }
            else NewLine();
        }

        return Append(text);
    }

    /// <summary>Termina la linea actual (si tiene algo) y asegura una linea en blanco; idempotente y nula al inicio del buffer o tras <c>{</c>.</summary>
    public CodeBuilder Blank()
    {
        Line();

        if (_sb.Length > 1 && _sb[_sb.Length - 2] is not ('\n' or '{')) NewLine();

        return this;
    }

    /// <summary><c>head</c> en su linea, <c>{</c> en la siguiente y sangria +1 (Allman); queda al inicio de linea.</summary>
    public CodeBuilder Open(string? head = null)
    {
        if (head != null) Line(head);

        Line("{");
        NewLine();
        return Push(false);
    }

    /// <summary><c>head { </c> en la linea actual; <see cref="Close"/> lo termina con <c> }</c> en la misma linea, o en linea propia si hubo saltos.</summary>
    public CodeBuilder OpenInline(string? head = null)
    {
        if (head != null) Append(head).Append(' ');

        Append("{ ");
        return Push(true);
    }

    /// <summary>Cierra el ultimo bloque ya desindentado; <paramref name="suffix"/> sigue a la llave (<c>});</c>, <c>}, token)</c>).</summary>
    public CodeBuilder Close(string? suffix = null)
    {
        Unindent();

        if (_depth < 64 && (_inline & (1UL << _depth)) != 0 && !_lineStart)
        {
            if (_sb[_sb.Length - 1] != ' ') _sb.Append(' ');
            _sb.Append('}');
        }
        else
        {
            Line("}");
        }

        return Append(suffix);
    }

    /// <summary>Quita los espacios y saltos finales.</summary>
    public CodeBuilder TrimEnd()
    {
        int end = _sb.Length;
        while (end > 0 && char.IsWhiteSpace(_sb[end - 1])) end--;
        Length = end;
        return this;
    }

    /// <summary>Insercion literal (sin sangria): para tokens o fragmentos ya sangrados.</summary>
    public CodeBuilder Insert(int index, string? text)
    {
        _sb.Insert(index, text);
        Sync();
        return this;
    }

    public CodeBuilder Remove(int index, int length)
    {
        _sb.Remove(index, length);
        Sync();
        return this;
    }

    /// <summary>Sustitucion literal de tokens (no de sangria).</summary>
    public CodeBuilder Replace(string oldValue, string? newValue)
    {
        _sb.Replace(oldValue, newValue);
        Sync();
        return this;
    }

    public string ToString(int start, int length) => _sb.ToString(start, length);

    public override string ToString() => _sb.ToString();

    private CodeBuilder Push(bool inline)
    {
        if (_depth < 64)
            _inline = inline ? _inline | (1UL << _depth) : _inline & ~(1UL << _depth);

        _depth++;
        return this;
    }

    private void Write(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return;

        if (_lineStart)
        {
            _sb.Append(' ', _depth * IndentSize);
            _lineStart = false;
        }

        _sb.Append(text);
    }

    private void NewLine()
    {
        int end = _sb.Length;
        while (end > 0 && _sb[end - 1] is ' ' or '\t') end--;
        _sb.Length = end;
        _sb.Append('\n');
        _lineStart = true;
        _inline = 0;
    }

    private void Sync() => _lineStart = _sb.Length == 0 || _sb[_sb.Length - 1] == '\n';
}
