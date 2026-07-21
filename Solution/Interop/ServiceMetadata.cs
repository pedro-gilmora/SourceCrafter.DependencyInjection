using Microsoft.CodeAnalysis;
using SourceCrafter.DependencyInjection.Constants;
using System;

namespace SourceCrafter.DependencyInjection;

/// <summary>
/// Representa los metadatos de un servicio de inyección de dependencias
/// </summary>
internal sealed class ServiceMetadata
{
    /// <summary>
    /// Obtiene o establece el ciclo de vida del servicio
    /// </summary>
    internal Lifetime Lifetime { get; set; } = Lifetime.Singleton;

    /// <summary>
    /// Obtiene o establece si el servicio está en caché
    /// </summary>
    internal bool IsCached { get; set; } = true;

    /// <summary>
    /// Obtiene o establece la clave del servicio
    /// </summary>
    internal string Key = string.Empty;

    /// <summary>
    /// Obtiene o establece la capacidad de disposición del servicio
    /// </summary>
    internal Disposability Disposability { get; set; }

    /// <summary>
    /// Obtiene o establece la capacidad de disposición del contenedor
    /// </summary>
    internal Disposability ContainerDisposability { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio está resuelto
    /// </summary>
    internal bool IsResolved { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio no está registrado
    /// </summary>
    internal bool NotRegistered { get; set; }

    /// <summary>
    /// Obtiene o establece si requiere conversión de disposición
    /// </summary>
    internal bool RequiresDisposabilityCast { get; set; }

    /// <summary>
    /// Obtiene o establece si es un parámetro de token de cancelación
    /// </summary>
    internal bool IsCancelTokenParam { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio es externo
    /// </summary>
    internal bool IsExternal { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio es asíncrono
    /// </summary>
    internal bool IsAsync { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio es una fábrica
    /// </summary>
    internal bool IsFactory { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio tiene clave
    /// </summary>
    internal bool IsKeyed { get; set; }

    /// <summary>
    /// Obtiene o establece si el servicio es transiente simple
    /// </summary>
    internal bool IsSimpleTransient { get; set; }

    /// <summary>
    /// Obtiene o establece si tiene dependencias con ámbito
    /// </summary>
    internal bool HasScopedDependencies { get; set; }

    /// <summary>
    /// Obtiene o establece el nombre completo del tipo
    /// </summary>
    internal string FullTypeName = string.Empty;

    /// <summary>
    /// Obtiene o establece el nombre del método de resolución
    /// </summary>
    internal string ResolverMethodName= string.Empty;

    /// <summary>
    /// Obtiene o establece el nombre del campo de caché
    /// </summary>
    internal string CacheField = string.Empty;

    /// <summary>
    /// Obtiene o establece el nombre del tipo de exportación
    /// </summary>
    internal string ExportTypeName = string.Empty;
} 