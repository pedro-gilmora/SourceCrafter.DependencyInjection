using System.Runtime.CompilerServices;

namespace Benchmarks.HandCoded.Devirtualization;

/// <summary>
/// La interfaz de resolucion, con la <b>misma firma</b> que emite el generador de este
/// repositorio (<c>SourceCrafter.DependencyInjection.IProvider&lt;TProvide&gt;</c>).
/// <para>
/// Se replica aqui, en vez de medir el contenedor generado, por una razon de metodo: el generador
/// rechaza en compilacion los sitios de llamada con el parametro de tipo abierto (SCDI11), que es
/// justo la mitad de esta tabla. Midiendo la forma a mano salen las cuatro filas bajo el mismo
/// arnes, y cualquier distancia posterior con el contenedor generado es margen del generador y no
/// una diferencia de diseno.
/// </para>
/// </summary>
public interface IProvider
{
    T GetService<T>() where T : notnull;
}
public interface IProvider<T> : IProvider where T : notnull
{
    T GetService();
}

/// <summary>
/// Contenedor escrito a mano de <b>100 dependencias</b> (70 de referencia, 30 de valor), en la
/// forma que emite el generador cuando se activa <c>genericApi</c>: una implementacion explicita
/// de <see cref="IProvider{T}"/> por servicio expuesto y un unico despachador
/// <see cref="Resolve{T}"/> cuyo cuerpo es la prueba de tipo
/// <c>this is IProvider&lt;TProvide&gt;</c>.
/// <para>
/// <b>Construccion eager en el constructor, a proposito.</b> La pereza ya tiene su propia tabla en
/// este banco. Si cada captador llevara aqui su lectura, su salto y su candado, la diferencia
/// entre formas de despacho -- que es lo unico que se mide -- quedaria por debajo del ruido de la
/// maquinaria de publicacion.
/// </para>
/// <para>
/// <b>Cien interfaces en la lista de bases no son decoracion.</b> La prueba de tipo la resuelve el
/// runtime recorriendo la tabla de interfaces del objeto, y su coste depende del tamano de esa
/// tabla. Un contenedor de tres servicios empata con cualquier cosa porque cabe entero en cache;
/// el caso que se paga en produccion es el de una aplicacion real, que es este.
/// </para>
/// </summary>
public sealed class CommerceContainer :
    // --- 26 valores hoja ---
    IProvider<RetryBudget>,
    IProvider<RequestTimeout>,
    IProvider<CacheTtl>,
    IProvider<PageSize>,
    IProvider<RateLimit>,
    IProvider<CircuitBreakerBudget>,
    IProvider<CurrencyCode>,
    IProvider<TaxRate>,
    IProvider<ShippingRate>,
    IProvider<DiscountRate>,
    IProvider<PricingRounding>,
    IProvider<InventoryThreshold>,
    IProvider<OrderNumberSeed>,
    IProvider<FraudScoreThreshold>,
    IProvider<CaptureDelay>,
    IProvider<RefundWindow>,
    IProvider<ShipmentSla>,
    IProvider<WarehouseSlot>,
    IProvider<CarrierCode>,
    IProvider<RegionCode>,
    IProvider<FeatureFlags>,
    IProvider<TelemetrySampling>,
    IProvider<ConnectionBudget>,
    IProvider<BatchSize>,
    IProvider<ClockSkew>,
    IProvider<SearchBoost>,
    // --- 4 valores compuestos ---
    IProvider<PricingPolicy>,
    IProvider<ResiliencePolicy>,
    IProvider<CartLimits>,
    IProvider<FulfillmentPolicy>,
    // --- 10 servicios de infraestructura, expuestos por interfaz ---
    IProvider<IClock>,
    IProvider<ILogSink>,
    IProvider<ITelemetry>,
    IProvider<ISerializer>,
    IProvider<ICache>,
    IProvider<IConnectionFactory>,
    IProvider<IHttpChannelFactory>,
    IProvider<IMessageBus>,
    IProvider<IBlobStore>,
    IProvider<ISecretStore>,
    // --- 12 repositorios ---
    IProvider<OrderRepository>,
    IProvider<CustomerRepository>,
    IProvider<ProductRepository>,
    IProvider<InventoryRepository>,
    IProvider<PricingRepository>,
    IProvider<CartRepository>,
    IProvider<PaymentRepository>,
    IProvider<ShipmentRepository>,
    IProvider<ReviewRepository>,
    IProvider<PromotionRepository>,
    IProvider<AuditRepository>,
    IProvider<OutboxRepository>,
    // --- 10 pasarelas ---
    IProvider<PaymentGateway>,
    IProvider<FraudGateway>,
    IProvider<TaxGateway>,
    IProvider<ShippingGateway>,
    IProvider<CarrierGateway>,
    IProvider<EmailGateway>,
    IProvider<SmsGateway>,
    IProvider<PushGateway>,
    IProvider<SearchGateway>,
    IProvider<RecommendationGateway>,
    // --- 12 servicios de dominio ---
    IProvider<PricingService>,
    IProvider<InventoryService>,
    IProvider<CartService>,
    IProvider<CatalogService>,
    IProvider<PromotionService>,
    IProvider<CheckoutService>,
    IProvider<OrderService>,
    IProvider<PaymentService>,
    IProvider<RefundService>,
    IProvider<ShipmentService>,
    IProvider<NotificationService>,
    IProvider<ReviewService>,
    // --- 6 validadores ---
    IProvider<CartValidator>,
    IProvider<AddressValidator>,
    IProvider<PaymentValidator>,
    IProvider<CouponValidator>,
    IProvider<InventoryValidator>,
    IProvider<OrderValidator>,
    // --- 8 manejadores ---
    IProvider<PlaceOrderHandler>,
    IProvider<CancelOrderHandler>,
    IProvider<CapturePaymentHandler>,
    IProvider<RefundPaymentHandler>,
    IProvider<ReserveInventoryHandler>,
    IProvider<CreateShipmentHandler>,
    IProvider<ApplyPromotionHandler>,
    IProvider<SubmitReviewHandler>,
    // --- 6 proyecciones ---
    IProvider<OrderSummaryProjection>,
    IProvider<CartSummaryProjection>,
    IProvider<CatalogProjection>,
    IProvider<InventoryProjection>,
    IProvider<RevenueProjection>,
    IProvider<ShipmentProjection>,
    // --- 6 endpoints ---
    IProvider<CheckoutEndpoint>,
    IProvider<CartEndpoint>,
    IProvider<CatalogEndpoint>,
    IProvider<OrderEndpoint>,
    IProvider<PaymentEndpoint>,
    IProvider<ShipmentEndpoint>
{
    // ===== Valores hoja: la configuracion, que en produccion vendria enlazada =====

    private readonly RetryBudget _retryBudget;
    private readonly RequestTimeout _requestTimeout;
    private readonly CacheTtl _cacheTtl;
    private readonly PageSize _pageSize;
    private readonly RateLimit _rateLimit;
    private readonly CircuitBreakerBudget _circuitBreakerBudget;
    private readonly CurrencyCode _currencyCode;
    private readonly TaxRate _taxRate;
    private readonly ShippingRate _shippingRate;
    private readonly DiscountRate _discountRate;
    private readonly PricingRounding _pricingRounding;
    private readonly InventoryThreshold _inventoryThreshold;
    private readonly OrderNumberSeed _orderNumberSeed;
    private readonly FraudScoreThreshold _fraudScoreThreshold;
    private readonly CaptureDelay _captureDelay;
    private readonly RefundWindow _refundWindow;
    private readonly ShipmentSla _shipmentSla;
    private readonly WarehouseSlot _warehouseSlot;
    private readonly CarrierCode _carrierCode;
    private readonly RegionCode _regionCode;
    private readonly FeatureFlags _featureFlags;
    private readonly TelemetrySampling _telemetrySampling;
    private readonly ConnectionBudget _connectionBudget;
    private readonly BatchSize _batchSize;
    private readonly ClockSkew _clockSkew;
    private readonly SearchBoost _searchBoost;

    // ===== Valores compuestos =====

    private readonly PricingPolicy _pricingPolicy;
    private readonly ResiliencePolicy _resiliencePolicy;
    private readonly CartLimits _cartLimits;
    private readonly FulfillmentPolicy _fulfillmentPolicy;

    // ===== Infraestructura =====

    private readonly IClock _clock;
    private readonly ILogSink _logSink;
    private readonly ITelemetry _telemetry;
    private readonly ISerializer _serializer;
    private readonly ICache _cache;
    private readonly IConnectionFactory _connections;
    private readonly IHttpChannelFactory _channels;
    private readonly IMessageBus _bus;
    private readonly IBlobStore _blobs;
    private readonly ISecretStore _secrets;

    // ===== Repositorios =====

    private readonly OrderRepository _orderRepository;
    private readonly CustomerRepository _customerRepository;
    private readonly ProductRepository _productRepository;
    private readonly InventoryRepository _inventoryRepository;
    private readonly PricingRepository _pricingRepository;
    private readonly CartRepository _cartRepository;
    private readonly PaymentRepository _paymentRepository;
    private readonly ShipmentRepository _shipmentRepository;
    private readonly ReviewRepository _reviewRepository;
    private readonly PromotionRepository _promotionRepository;
    private readonly AuditRepository _auditRepository;
    private readonly OutboxRepository _outboxRepository;

    // ===== Pasarelas =====

    private readonly PaymentGateway _paymentGateway;
    private readonly FraudGateway _fraudGateway;
    private readonly TaxGateway _taxGateway;
    private readonly ShippingGateway _shippingGateway;
    private readonly CarrierGateway _carrierGateway;
    private readonly EmailGateway _emailGateway;
    private readonly SmsGateway _smsGateway;
    private readonly PushGateway _pushGateway;
    private readonly SearchGateway _searchGateway;
    private readonly RecommendationGateway _recommendationGateway;

    // ===== Servicios de dominio =====

    private readonly PricingService _pricingService;
    private readonly InventoryService _inventoryService;
    private readonly CartService _cartService;
    private readonly CatalogService _catalogService;
    private readonly PromotionService _promotionService;
    private readonly CheckoutService _checkoutService;
    private readonly OrderService _orderService;
    private readonly PaymentService _paymentService;
    private readonly RefundService _refundService;
    private readonly ShipmentService _shipmentService;
    private readonly NotificationService _notificationService;
    private readonly ReviewService _reviewService;

    // ===== Validadores =====

    private readonly CartValidator _cartValidator;
    private readonly AddressValidator _addressValidator;
    private readonly PaymentValidator _paymentValidator;
    private readonly CouponValidator _couponValidator;
    private readonly InventoryValidator _inventoryValidator;
    private readonly OrderValidator _orderValidator;

    // ===== Manejadores =====

    private readonly PlaceOrderHandler _placeOrderHandler;
    private readonly CancelOrderHandler _cancelOrderHandler;
    private readonly CapturePaymentHandler _capturePaymentHandler;
    private readonly RefundPaymentHandler _refundPaymentHandler;
    private readonly ReserveInventoryHandler _reserveInventoryHandler;
    private readonly CreateShipmentHandler _createShipmentHandler;
    private readonly ApplyPromotionHandler _applyPromotionHandler;
    private readonly SubmitReviewHandler _submitReviewHandler;

    // ===== Proyecciones =====

    private readonly OrderSummaryProjection _orderSummaryProjection;
    private readonly CartSummaryProjection _cartSummaryProjection;
    private readonly CatalogProjection _catalogProjection;
    private readonly InventoryProjection _inventoryProjection;
    private readonly RevenueProjection _revenueProjection;
    private readonly ShipmentProjection _shipmentProjection;

    // ===== Endpoints =====

    private readonly CheckoutEndpoint _checkoutEndpoint;
    private readonly CartEndpoint _cartEndpoint;
    private readonly CatalogEndpoint _catalogEndpoint;
    private readonly OrderEndpoint _orderEndpoint;
    private readonly PaymentEndpoint _paymentEndpoint;
    private readonly ShipmentEndpoint _shipmentEndpoint;

    public CommerceContainer()
    {
        _retryBudget = new(3);
        _requestTimeout = new(2_000);
        _cacheTtl = new(60);
        _pageSize = new(50);
        _rateLimit = new(100);
        _circuitBreakerBudget = new(5);
        _currencyCode = new(978);
        _taxRate = new(0.21);
        _shippingRate = new(1.35);
        _discountRate = new(0.10);
        _pricingRounding = new(2);
        _inventoryThreshold = new(10);
        _orderNumberSeed = new(1_000_000L);
        _fraudScoreThreshold = new(0.85);
        _captureDelay = new(15);
        _refundWindow = new(30);
        _shipmentSla = new(48);
        _warehouseSlot = new(7);
        _carrierCode = new(3);
        _regionCode = new(34);
        _featureFlags = new(0b1011UL);
        _telemetrySampling = new(0.05);
        _connectionBudget = new(128);
        _batchSize = new(256);
        _clockSkew = new(1);
        _searchBoost = new(1.25);

        _pricingPolicy = new(_taxRate, _discountRate, _pricingRounding, _currencyCode);
        _resiliencePolicy = new(_retryBudget, _requestTimeout, _circuitBreakerBudget);
        _cartLimits = new(_rateLimit, _pageSize);
        _fulfillmentPolicy = new(_shippingRate, _shipmentSla, _carrierCode, _warehouseSlot);

        _clock = new SystemClock(_clockSkew);
        _logSink = new BufferedLogSink(_featureFlags);
        _telemetry = new SampledTelemetry(_telemetrySampling);
        _serializer = new Utf8Serializer();
        _cache = new InProcessCache(_cacheTtl, _serializer);
        _connections = new PooledConnectionFactory(_connectionBudget);
        _channels = new ResilientHttpChannelFactory(_resiliencePolicy);
        _bus = new OutboxMessageBus(_batchSize, _serializer);
        _blobs = new ChunkedBlobStore(_resiliencePolicy);
        _secrets = new CachedSecretStore(_cache);

        _orderRepository = new(_connections, _pageSize);
        _customerRepository = new(_connections, _pageSize);
        _productRepository = new(_connections, _pageSize, _cache);
        _inventoryRepository = new(_connections, _inventoryThreshold);
        _pricingRepository = new(_connections, _pricingRounding);
        _cartRepository = new(_connections, _cartLimits);
        _paymentRepository = new(_connections, _captureDelay);
        _shipmentRepository = new(_connections, _shipmentSla);
        _reviewRepository = new(_connections, _pageSize);
        _promotionRepository = new(_connections, _discountRate);
        _auditRepository = new(_connections, _clock);
        _outboxRepository = new(_connections, _batchSize);

        _paymentGateway = new(_channels, _resiliencePolicy, _currencyCode);
        _fraudGateway = new(_channels, _fraudScoreThreshold);
        _taxGateway = new(_channels, _taxRate, _regionCode);
        _shippingGateway = new(_channels, _fulfillmentPolicy);
        _carrierGateway = new(_channels, _carrierCode);
        _emailGateway = new(_channels, _rateLimit);
        _smsGateway = new(_channels, _rateLimit);
        _pushGateway = new(_channels, _rateLimit);
        _searchGateway = new(_channels, _searchBoost, _pageSize);
        _recommendationGateway = new(_channels, _searchBoost);

        _pricingService = new(_pricingRepository, _pricingPolicy);
        _inventoryService = new(_inventoryRepository, _inventoryThreshold, _telemetry);
        _cartService = new(_cartRepository, _cartLimits, _pricingService);
        _catalogService = new(_productRepository, _cache, _pageSize);
        _promotionService = new(_promotionRepository, _discountRate);
        _checkoutService = new(_cartService, _pricingService, _inventoryService, _taxGateway);
        _orderService = new(_orderRepository, _orderNumberSeed, _clock);
        _paymentService = new(_paymentRepository, _paymentGateway, _captureDelay);
        _refundService = new(_paymentRepository, _refundWindow, _paymentGateway);
        _shipmentService = new(_shipmentRepository, _shippingGateway, _carrierGateway, _fulfillmentPolicy);
        _notificationService = new(_emailGateway, _smsGateway, _pushGateway);
        _reviewService = new(_reviewRepository, _customerRepository);

        _cartValidator = new(_cartLimits);
        _addressValidator = new(_regionCode);
        _paymentValidator = new(_currencyCode, _fraudGateway);
        _couponValidator = new(_promotionRepository, _discountRate);
        _inventoryValidator = new(_inventoryRepository, _inventoryThreshold);
        _orderValidator = new(_orderRepository, _customerRepository);

        _placeOrderHandler = new(_checkoutService, _orderService, _orderValidator, _bus);
        _cancelOrderHandler = new(_orderService, _refundService, _bus);
        _capturePaymentHandler = new(_paymentService, _paymentValidator, _telemetry);
        _refundPaymentHandler = new(_refundService, _refundWindow);
        _reserveInventoryHandler = new(_inventoryService, _inventoryValidator);
        _createShipmentHandler = new(_shipmentService, _addressValidator);
        _applyPromotionHandler = new(_promotionService, _couponValidator, _cartValidator);
        _submitReviewHandler = new(_reviewService, _cache);

        _orderSummaryProjection = new(_orderRepository, _pricingService);
        _cartSummaryProjection = new(_cartRepository, _cartLimits);
        _catalogProjection = new(_productRepository, _searchGateway);
        _inventoryProjection = new(_inventoryRepository, _inventoryThreshold);
        _revenueProjection = new(_orderRepository, _pricingPolicy);
        _shipmentProjection = new(_shipmentRepository, _shipmentSla);

        _checkoutEndpoint = new(_placeOrderHandler, _applyPromotionHandler, _cartSummaryProjection);
        _cartEndpoint = new(_cartService, _cartValidator, _cartSummaryProjection);
        _catalogEndpoint = new(_catalogService, _catalogProjection, _recommendationGateway);
        _orderEndpoint = new(_orderSummaryProjection, _cancelOrderHandler, _orderService);
        _paymentEndpoint = new(_capturePaymentHandler, _refundPaymentHandler, _paymentValidator);
        _shipmentEndpoint = new(_createShipmentHandler, _shipmentProjection, _shipmentService);
    }

    /// <summary>
    /// Los miembros con nombre: el <b>suelo</b> de la tabla. No hay prueba de tipo ni llamada de
    /// interfaz, solo la lectura del campo. Es lo que consigue el interceptor del generador cuando
    /// puede enlazar el contenedor concreto en el sitio de llamada.
    /// </summary>
    public OrderEndpoint OrderEndpoint => _orderEndpoint;

    /// <inheritdoc cref="OrderEndpoint"/>
    public CheckoutService CheckoutService => _checkoutService;

    /// <inheritdoc cref="OrderEndpoint"/>
    public PricingPolicy PricingPolicy => _pricingPolicy;

    /// <inheritdoc cref="OrderEndpoint"/>
    public CartLimits CartLimits => _cartLimits;

    public T GetService<T>() where T : notnull
    {
        return ((IProvider<T>)(IProvider)this).GetService<T>();
    }

    /// <summary>
    /// El despachador generico, identico en forma al que emite el generador: una sola prueba de
    /// tipo contra la tabla de interfaces. No compara nombres de tipo.
    /// <para>
    /// Se llama <c>Resolve</c> y no <c>GetRequiredService</c> porque el generador esta referenciado
    /// como analizador en este proyecto y reclama por nombre cualquier llamada con esa firma,
    /// incluso sobre un tipo escrito a mano: rechazaria en compilacion (SCDI11) los sitios con el
    /// parametro de tipo abierto, que son la mitad de la tabla.
    /// </para>
    /// </summary>
    public T Resolve<T>() where T : notnull
    {
        return (this as IProvider<T>)!.GetService();
    }

    /// <summary>
    /// La misma resolucion con <b>conversion directa</b> en vez de prueba de tipo. Existe solo
    /// para medir si la prueba cuesta algo frente al cast.
    /// <para>
    /// El trabajo caro es el mismo en las dos: recorrer la tabla de interfaces del objeto. Cambia
    /// el helper (<c>isinst</c> frente a <c>castclass</c>) y el epilogo -- una prueba de nulo y un
    /// salto frente a la ruta de lanzamiento de <see cref="InvalidCastException"/>.
    /// </para>
    /// <para>
    /// <b>No es una alternativa real para el generador</b>, y por eso no se propone como tal: un
    /// servicio no registrado daria <see cref="InvalidCastException"/> en vez de un mensaje que
    /// diga que falta el registro, <c>GetService&lt;TProvide&gt;()</c> no podria devolver <c>null</c>
    /// como documenta MS DI, y el despacho por clave -- que encadena varias pruebas antes de
    /// rendirse -- no se puede escribir con conversiones. La fila es una medicion, no una
    /// propuesta.
    /// </para>
    /// <para>
    /// El cast pasa por <c>object</c> porque el compilador rechaza (CS0030) la conversion directa
    /// del tipo concreto al <c>IProvider&lt;TProvide&gt;</c> abierto: con <c>TProvide</c> sin relacion declarada
    /// con el receptor, no puede probar que exista una instanciacion valida. No es cosa de
    /// <c>sealed</c> -- quitarlo no cambia nada. Se puede acallar restringiendo el parametro
    /// (<c>where TOut : TProvide</c> sobre un contenedor generico), pero eso solo convence al compilador:
    /// en ejecucion sigue fallando con <see cref="InvalidCastException"/> para cualquier derivado,
    /// porque la implementacion de esa instanciacion no existe. El paso por <c>object</c> no cuesta
    /// nada en tiempo de ejecucion -- el receptor ya es una referencia -- asi que no contamina la
    /// medicion.
    /// </para>
    /// </summary>
    //public T ResolveByCast<T>() where T : notnull => ((IProvider<T>)this).GetService();

    /// <summary>
    /// La tercera forma: <b>sin comprobacion ninguna</b>. <see cref="Unsafe.As{T}(object)"/>
    /// reinterpreta el receptor sin consultar la tabla de interfaces, asi que lo unico que queda
    /// en pie es la llamada de interfaz.
    /// <para>
    /// <b>Es una sonda de medicion, no codigo utilizable.</b> Con un <c>TProvide</c> no registrado no
    /// lanza: salta a una ranura arbitraria de la tabla, con el desenlace que corresponda. Existe
    /// para responder una sola pregunta que las otras dos filas no pueden separar: de los ~18 ns
    /// del resolver abstracto, <b>cuanto es la busqueda del tipo y cuanto la llamada</b>. La
    /// distancia entre esta fila y las dos anteriores es el precio de la busqueda; lo que esta
    /// fila marca es el suelo irreducible del despacho por interfaz.
    /// </para>
    /// </summary>
    public T ResolveUnchecked<T>() where T : notnull => Unsafe.As<IProvider<T>>(this).GetService();

    // ===== Implementaciones explicitas: una por servicio expuesto =====

    RetryBudget IProvider<RetryBudget>.GetService() => _retryBudget;
    RequestTimeout IProvider<RequestTimeout>.GetService() => _requestTimeout;
    CacheTtl IProvider<CacheTtl>.GetService() => _cacheTtl;
    PageSize IProvider<PageSize>.GetService() => _pageSize;
    RateLimit IProvider<RateLimit>.GetService() => _rateLimit;
    CircuitBreakerBudget IProvider<CircuitBreakerBudget>.GetService() => _circuitBreakerBudget;
    CurrencyCode IProvider<CurrencyCode>.GetService() => _currencyCode;
    TaxRate IProvider<TaxRate>.GetService() => _taxRate;
    ShippingRate IProvider<ShippingRate>.GetService() => _shippingRate;
    DiscountRate IProvider<DiscountRate>.GetService() => _discountRate;
    PricingRounding IProvider<PricingRounding>.GetService() => _pricingRounding;
    InventoryThreshold IProvider<InventoryThreshold>.GetService() => _inventoryThreshold;
    OrderNumberSeed IProvider<OrderNumberSeed>.GetService() => _orderNumberSeed;
    FraudScoreThreshold IProvider<FraudScoreThreshold>.GetService() => _fraudScoreThreshold;
    CaptureDelay IProvider<CaptureDelay>.GetService() => _captureDelay;
    RefundWindow IProvider<RefundWindow>.GetService() => _refundWindow;
    ShipmentSla IProvider<ShipmentSla>.GetService() => _shipmentSla;
    WarehouseSlot IProvider<WarehouseSlot>.GetService() => _warehouseSlot;
    CarrierCode IProvider<CarrierCode>.GetService() => _carrierCode;
    RegionCode IProvider<RegionCode>.GetService() => _regionCode;
    FeatureFlags IProvider<FeatureFlags>.GetService() => _featureFlags;
    TelemetrySampling IProvider<TelemetrySampling>.GetService() => _telemetrySampling;
    ConnectionBudget IProvider<ConnectionBudget>.GetService() => _connectionBudget;
    BatchSize IProvider<BatchSize>.GetService() => _batchSize;
    ClockSkew IProvider<ClockSkew>.GetService() => _clockSkew;
    SearchBoost IProvider<SearchBoost>.GetService() => _searchBoost;

    PricingPolicy IProvider<PricingPolicy>.GetService() => _pricingPolicy;
    ResiliencePolicy IProvider<ResiliencePolicy>.GetService() => _resiliencePolicy;
    CartLimits IProvider<CartLimits>.GetService() => _cartLimits;
    FulfillmentPolicy IProvider<FulfillmentPolicy>.GetService() => _fulfillmentPolicy;

    IClock IProvider<IClock>.GetService() => _clock;
    ILogSink IProvider<ILogSink>.GetService() => _logSink;
    ITelemetry IProvider<ITelemetry>.GetService() => _telemetry;
    ISerializer IProvider<ISerializer>.GetService() => _serializer;
    ICache IProvider<ICache>.GetService() => _cache;
    IConnectionFactory IProvider<IConnectionFactory>.GetService() => _connections;
    IHttpChannelFactory IProvider<IHttpChannelFactory>.GetService() => _channels;
    IMessageBus IProvider<IMessageBus>.GetService() => _bus;
    IBlobStore IProvider<IBlobStore>.GetService() => _blobs;
    ISecretStore IProvider<ISecretStore>.GetService() => _secrets;

    OrderRepository IProvider<OrderRepository>.GetService() => _orderRepository;
    CustomerRepository IProvider<CustomerRepository>.GetService() => _customerRepository;
    ProductRepository IProvider<ProductRepository>.GetService() => _productRepository;
    InventoryRepository IProvider<InventoryRepository>.GetService() => _inventoryRepository;
    PricingRepository IProvider<PricingRepository>.GetService() => _pricingRepository;
    CartRepository IProvider<CartRepository>.GetService() => _cartRepository;
    PaymentRepository IProvider<PaymentRepository>.GetService() => _paymentRepository;
    ShipmentRepository IProvider<ShipmentRepository>.GetService() => _shipmentRepository;
    ReviewRepository IProvider<ReviewRepository>.GetService() => _reviewRepository;
    PromotionRepository IProvider<PromotionRepository>.GetService() => _promotionRepository;
    AuditRepository IProvider<AuditRepository>.GetService() => _auditRepository;
    OutboxRepository IProvider<OutboxRepository>.GetService() => _outboxRepository;

    PaymentGateway IProvider<PaymentGateway>.GetService() => _paymentGateway;
    FraudGateway IProvider<FraudGateway>.GetService() => _fraudGateway;
    TaxGateway IProvider<TaxGateway>.GetService() => _taxGateway;
    ShippingGateway IProvider<ShippingGateway>.GetService() => _shippingGateway;
    CarrierGateway IProvider<CarrierGateway>.GetService() => _carrierGateway;
    EmailGateway IProvider<EmailGateway>.GetService() => _emailGateway;
    SmsGateway IProvider<SmsGateway>.GetService() => _smsGateway;
    PushGateway IProvider<PushGateway>.GetService() => _pushGateway;
    SearchGateway IProvider<SearchGateway>.GetService() => _searchGateway;
    RecommendationGateway IProvider<RecommendationGateway>.GetService() => _recommendationGateway;

    PricingService IProvider<PricingService>.GetService() => _pricingService;
    InventoryService IProvider<InventoryService>.GetService() => _inventoryService;
    CartService IProvider<CartService>.GetService() => _cartService;
    CatalogService IProvider<CatalogService>.GetService() => _catalogService;
    PromotionService IProvider<PromotionService>.GetService() => _promotionService;
    CheckoutService IProvider<CheckoutService>.GetService() => _checkoutService;
    OrderService IProvider<OrderService>.GetService() => _orderService;
    PaymentService IProvider<PaymentService>.GetService() => _paymentService;
    RefundService IProvider<RefundService>.GetService() => _refundService;
    ShipmentService IProvider<ShipmentService>.GetService() => _shipmentService;
    NotificationService IProvider<NotificationService>.GetService() => _notificationService;
    ReviewService IProvider<ReviewService>.GetService() => _reviewService;

    CartValidator IProvider<CartValidator>.GetService() => _cartValidator;
    AddressValidator IProvider<AddressValidator>.GetService() => _addressValidator;
    PaymentValidator IProvider<PaymentValidator>.GetService() => _paymentValidator;
    CouponValidator IProvider<CouponValidator>.GetService() => _couponValidator;
    InventoryValidator IProvider<InventoryValidator>.GetService() => _inventoryValidator;
    OrderValidator IProvider<OrderValidator>.GetService() => _orderValidator;

    PlaceOrderHandler IProvider<PlaceOrderHandler>.GetService() => _placeOrderHandler;
    CancelOrderHandler IProvider<CancelOrderHandler>.GetService() => _cancelOrderHandler;
    CapturePaymentHandler IProvider<CapturePaymentHandler>.GetService() => _capturePaymentHandler;
    RefundPaymentHandler IProvider<RefundPaymentHandler>.GetService() => _refundPaymentHandler;
    ReserveInventoryHandler IProvider<ReserveInventoryHandler>.GetService() => _reserveInventoryHandler;
    CreateShipmentHandler IProvider<CreateShipmentHandler>.GetService() => _createShipmentHandler;
    ApplyPromotionHandler IProvider<ApplyPromotionHandler>.GetService() => _applyPromotionHandler;
    SubmitReviewHandler IProvider<SubmitReviewHandler>.GetService() => _submitReviewHandler;

    OrderSummaryProjection IProvider<OrderSummaryProjection>.GetService() => _orderSummaryProjection;
    CartSummaryProjection IProvider<CartSummaryProjection>.GetService() => _cartSummaryProjection;
    CatalogProjection IProvider<CatalogProjection>.GetService() => _catalogProjection;
    InventoryProjection IProvider<InventoryProjection>.GetService() => _inventoryProjection;
    RevenueProjection IProvider<RevenueProjection>.GetService() => _revenueProjection;
    ShipmentProjection IProvider<ShipmentProjection>.GetService() => _shipmentProjection;

    CheckoutEndpoint IProvider<CheckoutEndpoint>.GetService() => _checkoutEndpoint;
    CartEndpoint IProvider<CartEndpoint>.GetService() => _cartEndpoint;
    CatalogEndpoint IProvider<CatalogEndpoint>.GetService() => _catalogEndpoint;
    OrderEndpoint IProvider<OrderEndpoint>.GetService() => _orderEndpoint;
    PaymentEndpoint IProvider<PaymentEndpoint>.GetService() => _paymentEndpoint;
    ShipmentEndpoint IProvider<ShipmentEndpoint>.GetService() => _shipmentEndpoint;
}
