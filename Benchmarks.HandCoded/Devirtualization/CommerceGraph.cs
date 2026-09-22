namespace Benchmarks.HandCoded.Devirtualization;

/// <summary>
/// Grafo de 100 dependencias de un backend de <b>checkout y fulfillment</b>: configuracion,
/// infraestructura, repositorios, pasarelas, servicios de dominio, validadores, manejadores,
/// proyecciones y endpoints.
/// <para>
/// No es un grafo sintetico de <c>Leaf</c>/<c>Level1</c>. La pregunta que responde el banco
/// -- <b>cuando devirtualiza el JIT el despachador generico</b> -- depende de cuantas interfaces
/// implementa el contenedor y de si el parametro de tipo es referencia o valor. Ambas cosas solo
/// se manifiestan a escala: con tres servicios la tabla de interfaces cabe en cache y cualquier
/// forma de despacho empata.
/// </para>
/// <para>
/// <b>El reparto referencia/valor es deliberado: 70 referencias y 30 valores.</b> Es el eje del
/// estudio. Los genericos sobre referencia comparten codigo (<c>__Canon</c>), asi que la prueba
/// <c>this is IProvider&lt;TOut&gt;</c> sobrevive a la compilacion y el JIT no puede plegarla;
/// los genericos sobre valor se especializan por instanciacion, asi que la misma prueba es
/// constante en tiempo de JIT y el despacho entero puede desaparecer. Medir solo con clases
/// hubiera reportado la mitad lenta como si fuera la unica.
/// </para>
/// <para>
/// <b>Los tipos combinados no son adorno.</b> <see cref="PricingPolicy"/>,
/// <see cref="ResiliencePolicy"/>, <see cref="CartLimits"/> y <see cref="FulfillmentPolicy"/> son
/// structs construidos a partir de otros structs, y la mayoria de los servicios de referencia
/// toman a la vez dependencias de referencia y de valor. Es la forma real de un backend con
/// configuracion tipada, y es la unica que obliga al generador a emitir las dos familias de
/// instanciacion en el mismo contenedor.
/// </para>
/// <para>
/// Todos los constructores son triviales y todos los registros son singleton. El banco mide
/// <b>despacho</b>, no construccion: si un servicio costara mas que otro, la fila que lo resuelve
/// saldria peor y se leeria como si su forma de despacho fuera mas lenta.
/// </para>
/// </summary>
// ===== Configuracion: 26 tipos de valor hoja =====
// Sus valores los fija el contenedor en su constructor, que es de donde vienen en produccion:
// configuracion enlazada, no nodos del grafo.

public readonly record struct RetryBudget(int MaxAttempts);
public readonly record struct RequestTimeout(int Milliseconds);
public readonly record struct CacheTtl(int Seconds);
public readonly record struct PageSize(int Items);
public readonly record struct RateLimit(int PerSecond);
public readonly record struct CircuitBreakerBudget(int Failures);
public readonly record struct CurrencyCode(int Iso4217);
public readonly record struct TaxRate(double Value);
public readonly record struct ShippingRate(double PerKilo);
public readonly record struct DiscountRate(double Value);
public readonly record struct PricingRounding(int Decimals);
public readonly record struct InventoryThreshold(int Units);
public readonly record struct OrderNumberSeed(long Value);
public readonly record struct FraudScoreThreshold(double Value);
public readonly record struct CaptureDelay(int Minutes);
public readonly record struct RefundWindow(int Days);
public readonly record struct ShipmentSla(int Hours);
public readonly record struct WarehouseSlot(int Id);
public readonly record struct CarrierCode(int Id);
public readonly record struct RegionCode(int Id);
public readonly record struct FeatureFlags(ulong Mask);
public readonly record struct TelemetrySampling(double Ratio);
public readonly record struct ConnectionBudget(int MaxPooled);
public readonly record struct BatchSize(int Items);
public readonly record struct ClockSkew(int Seconds);
public readonly record struct SearchBoost(double Factor);

// ===== Configuracion compuesta: 4 tipos de valor con dependencias de valor =====
// Estos si los construye el contenedor. Son la celda "valor que depende de valor" del estudio.

public readonly record struct PricingPolicy(TaxRate Tax, DiscountRate Discount, PricingRounding Rounding, CurrencyCode Currency);
public readonly record struct ResiliencePolicy(RetryBudget Retry, RequestTimeout Timeout, CircuitBreakerBudget Breaker);
public readonly record struct CartLimits(RateLimit Rate, PageSize Page);
public readonly record struct FulfillmentPolicy(ShippingRate Rate, ShipmentSla Sla, CarrierCode Carrier, WarehouseSlot Slot);

// ===== Infraestructura: 10 registros interfaz -> implementacion =====
// Son los unicos que se registran por interfaz. Basta con ellos para que el contenedor tenga
// que emitir IProvider<TProvide> sobre tipos que no son la propia implementacion.

public interface IClock;
public sealed class SystemClock(ClockSkew skew) : IClock
{
    public ClockSkew Skew { get; } = skew;
}

public interface ILogSink;
public sealed class BufferedLogSink(FeatureFlags flags) : ILogSink
{
    public FeatureFlags Flags { get; } = flags;
}

public interface ITelemetry;
public sealed class SampledTelemetry(TelemetrySampling sampling) : ITelemetry
{
    public TelemetrySampling Sampling { get; } = sampling;
}

public interface ISerializer;
public sealed class Utf8Serializer : ISerializer;

public interface ICache;
public sealed class InProcessCache(CacheTtl ttl, ISerializer serializer) : ICache
{
    public CacheTtl Ttl { get; } = ttl;
    public ISerializer Serializer { get; } = serializer;
}

public interface IConnectionFactory;
public sealed class PooledConnectionFactory(ConnectionBudget budget) : IConnectionFactory
{
    public ConnectionBudget Budget { get; } = budget;
}

public interface IHttpChannelFactory;
public sealed class ResilientHttpChannelFactory(ResiliencePolicy policy) : IHttpChannelFactory
{
    public ResiliencePolicy Policy { get; } = policy;
}

public interface IMessageBus;
public sealed class OutboxMessageBus(BatchSize batch, ISerializer serializer) : IMessageBus
{
    public BatchSize Batch { get; } = batch;
    public ISerializer Serializer { get; } = serializer;
}

public interface IBlobStore;
public sealed class ChunkedBlobStore(ResiliencePolicy policy) : IBlobStore
{
    public ResiliencePolicy Policy { get; } = policy;
}

public interface ISecretStore;
public sealed class CachedSecretStore(ICache cache) : ISecretStore
{
    public ICache Cache { get; } = cache;
}

// ===== Repositorios: 12 =====

public sealed class OrderRepository(IConnectionFactory connections, PageSize page)
{
    public IConnectionFactory Connections { get; } = connections;
    public PageSize Page { get; } = page;
}

public sealed class CustomerRepository(IConnectionFactory connections, PageSize page)
{
    public IConnectionFactory Connections { get; } = connections;
    public PageSize Page { get; } = page;
}

public sealed class ProductRepository(IConnectionFactory connections, PageSize page, ICache cache)
{
    public IConnectionFactory Connections { get; } = connections;
    public PageSize Page { get; } = page;
    public ICache Cache { get; } = cache;
}

public sealed class InventoryRepository(IConnectionFactory connections, InventoryThreshold threshold)
{
    public IConnectionFactory Connections { get; } = connections;
    public InventoryThreshold Threshold { get; } = threshold;
}

public sealed class PricingRepository(IConnectionFactory connections, PricingRounding rounding)
{
    public IConnectionFactory Connections { get; } = connections;
    public PricingRounding Rounding { get; } = rounding;
}

public sealed class CartRepository(IConnectionFactory connections, CartLimits limits)
{
    public IConnectionFactory Connections { get; } = connections;
    public CartLimits Limits { get; } = limits;
}

public sealed class PaymentRepository(IConnectionFactory connections, CaptureDelay delay)
{
    public IConnectionFactory Connections { get; } = connections;
    public CaptureDelay Delay { get; } = delay;
}

public sealed class ShipmentRepository(IConnectionFactory connections, ShipmentSla sla)
{
    public IConnectionFactory Connections { get; } = connections;
    public ShipmentSla Sla { get; } = sla;
}

public sealed class ReviewRepository(IConnectionFactory connections, PageSize page)
{
    public IConnectionFactory Connections { get; } = connections;
    public PageSize Page { get; } = page;
}

public sealed class PromotionRepository(IConnectionFactory connections, DiscountRate discount)
{
    public IConnectionFactory Connections { get; } = connections;
    public DiscountRate Discount { get; } = discount;
}

public sealed class AuditRepository(IConnectionFactory connections, IClock clock)
{
    public IConnectionFactory Connections { get; } = connections;
    public IClock Clock { get; } = clock;
}

public sealed class OutboxRepository(IConnectionFactory connections, BatchSize batch)
{
    public IConnectionFactory Connections { get; } = connections;
    public BatchSize Batch { get; } = batch;
}

// ===== Pasarelas: 10 =====

public sealed class PaymentGateway(IHttpChannelFactory channels, ResiliencePolicy policy, CurrencyCode currency)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public ResiliencePolicy Policy { get; } = policy;
    public CurrencyCode Currency { get; } = currency;
}

public sealed class FraudGateway(IHttpChannelFactory channels, FraudScoreThreshold threshold)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public FraudScoreThreshold Threshold { get; } = threshold;
}

public sealed class TaxGateway(IHttpChannelFactory channels, TaxRate rate, RegionCode region)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public TaxRate Rate { get; } = rate;
    public RegionCode Region { get; } = region;
}

public sealed class ShippingGateway(IHttpChannelFactory channels, FulfillmentPolicy policy)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public FulfillmentPolicy Policy { get; } = policy;
}

public sealed class CarrierGateway(IHttpChannelFactory channels, CarrierCode carrier)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public CarrierCode Carrier { get; } = carrier;
}

public sealed class EmailGateway(IHttpChannelFactory channels, RateLimit limit)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public RateLimit Limit { get; } = limit;
}

public sealed class SmsGateway(IHttpChannelFactory channels, RateLimit limit)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public RateLimit Limit { get; } = limit;
}

public sealed class PushGateway(IHttpChannelFactory channels, RateLimit limit)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public RateLimit Limit { get; } = limit;
}

public sealed class SearchGateway(IHttpChannelFactory channels, SearchBoost boost, PageSize page)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public SearchBoost Boost { get; } = boost;
    public PageSize Page { get; } = page;
}

public sealed class RecommendationGateway(IHttpChannelFactory channels, SearchBoost boost)
{
    public IHttpChannelFactory Channels { get; } = channels;
    public SearchBoost Boost { get; } = boost;
}

// ===== Servicios de dominio: 12 =====

public sealed class PricingService(PricingRepository repository, PricingPolicy policy)
{
    public PricingRepository Repository { get; } = repository;
    public PricingPolicy Policy { get; } = policy;
}

public sealed class InventoryService(InventoryRepository repository, InventoryThreshold threshold, ITelemetry telemetry)
{
    public InventoryRepository Repository { get; } = repository;
    public InventoryThreshold Threshold { get; } = threshold;
    public ITelemetry Telemetry { get; } = telemetry;
}

public sealed class CartService(CartRepository repository, CartLimits limits, PricingService pricing)
{
    public CartRepository Repository { get; } = repository;
    public CartLimits Limits { get; } = limits;
    public PricingService Pricing { get; } = pricing;
}

public sealed class CatalogService(ProductRepository repository, ICache cache, PageSize page)
{
    public ProductRepository Repository { get; } = repository;
    public ICache Cache { get; } = cache;
    public PageSize Page { get; } = page;
}

public sealed class PromotionService(PromotionRepository repository, DiscountRate discount)
{
    public PromotionRepository Repository { get; } = repository;
    public DiscountRate Discount { get; } = discount;
}

/// <summary>
/// La celda "combinada" del estudio: tres dependencias de referencia y una pasarela que a su vez
/// arrastra dos tipos de valor. Es el servicio que se resuelve en la fila <c>Combined</c>.
/// </summary>
public sealed class CheckoutService(CartService cart, PricingService pricing, InventoryService inventory, TaxGateway tax)
{
    public CartService Cart { get; } = cart;
    public PricingService Pricing { get; } = pricing;
    public InventoryService Inventory { get; } = inventory;
    public TaxGateway Tax { get; } = tax;
}

public sealed class OrderService(OrderRepository repository, OrderNumberSeed seed, IClock clock)
{
    public OrderRepository Repository { get; } = repository;
    public OrderNumberSeed Seed { get; } = seed;
    public IClock Clock { get; } = clock;
}

public sealed class PaymentService(PaymentRepository repository, PaymentGateway gateway, CaptureDelay delay)
{
    public PaymentRepository Repository { get; } = repository;
    public PaymentGateway Gateway { get; } = gateway;
    public CaptureDelay Delay { get; } = delay;
}

public sealed class RefundService(PaymentRepository repository, RefundWindow window, PaymentGateway gateway)
{
    public PaymentRepository Repository { get; } = repository;
    public RefundWindow Window { get; } = window;
    public PaymentGateway Gateway { get; } = gateway;
}

public sealed class ShipmentService(ShipmentRepository repository, ShippingGateway shipping, CarrierGateway carrier, FulfillmentPolicy policy)
{
    public ShipmentRepository Repository { get; } = repository;
    public ShippingGateway Shipping { get; } = shipping;
    public CarrierGateway Carrier { get; } = carrier;
    public FulfillmentPolicy Policy { get; } = policy;
}

public sealed class NotificationService(EmailGateway email, SmsGateway sms, PushGateway push)
{
    public EmailGateway Email { get; } = email;
    public SmsGateway Sms { get; } = sms;
    public PushGateway Push { get; } = push;
}

public sealed class ReviewService(ReviewRepository reviews, CustomerRepository customers)
{
    public ReviewRepository Reviews { get; } = reviews;
    public CustomerRepository Customers { get; } = customers;
}

// ===== Validadores: 6 =====

public sealed class CartValidator(CartLimits limits)
{
    public CartLimits Limits { get; } = limits;
}

public sealed class AddressValidator(RegionCode region)
{
    public RegionCode Region { get; } = region;
}

public sealed class PaymentValidator(CurrencyCode currency, FraudGateway fraud)
{
    public CurrencyCode Currency { get; } = currency;
    public FraudGateway Fraud { get; } = fraud;
}

public sealed class CouponValidator(PromotionRepository promotions, DiscountRate discount)
{
    public PromotionRepository Promotions { get; } = promotions;
    public DiscountRate Discount { get; } = discount;
}

public sealed class InventoryValidator(InventoryRepository repository, InventoryThreshold threshold)
{
    public InventoryRepository Repository { get; } = repository;
    public InventoryThreshold Threshold { get; } = threshold;
}

public sealed class OrderValidator(OrderRepository orders, CustomerRepository customers)
{
    public OrderRepository Orders { get; } = orders;
    public CustomerRepository Customers { get; } = customers;
}

// ===== Manejadores: 8 =====

public sealed class PlaceOrderHandler(CheckoutService checkout, OrderService orders, OrderValidator validator, IMessageBus bus)
{
    public CheckoutService Checkout { get; } = checkout;
    public OrderService Orders { get; } = orders;
    public OrderValidator Validator { get; } = validator;
    public IMessageBus Bus { get; } = bus;
}

public sealed class CancelOrderHandler(OrderService orders, RefundService refunds, IMessageBus bus)
{
    public OrderService Orders { get; } = orders;
    public RefundService Refunds { get; } = refunds;
    public IMessageBus Bus { get; } = bus;
}

public sealed class CapturePaymentHandler(PaymentService payments, PaymentValidator validator, ITelemetry telemetry)
{
    public PaymentService Payments { get; } = payments;
    public PaymentValidator Validator { get; } = validator;
    public ITelemetry Telemetry { get; } = telemetry;
}

public sealed class RefundPaymentHandler(RefundService refunds, RefundWindow window)
{
    public RefundService Refunds { get; } = refunds;
    public RefundWindow Window { get; } = window;
}

public sealed class ReserveInventoryHandler(InventoryService inventory, InventoryValidator validator)
{
    public InventoryService Inventory { get; } = inventory;
    public InventoryValidator Validator { get; } = validator;
}

public sealed class CreateShipmentHandler(ShipmentService shipments, AddressValidator validator)
{
    public ShipmentService Shipments { get; } = shipments;
    public AddressValidator Validator { get; } = validator;
}

public sealed class ApplyPromotionHandler(PromotionService promotions, CouponValidator coupons, CartValidator carts)
{
    public PromotionService Promotions { get; } = promotions;
    public CouponValidator Coupons { get; } = coupons;
    public CartValidator Carts { get; } = carts;
}

public sealed class SubmitReviewHandler(ReviewService reviews, ICache cache)
{
    public ReviewService Reviews { get; } = reviews;
    public ICache Cache { get; } = cache;
}

// ===== Proyecciones: 6 =====

public sealed class OrderSummaryProjection(OrderRepository orders, PricingService pricing)
{
    public OrderRepository Orders { get; } = orders;
    public PricingService Pricing { get; } = pricing;
}

public sealed class CartSummaryProjection(CartRepository carts, CartLimits limits)
{
    public CartRepository Carts { get; } = carts;
    public CartLimits Limits { get; } = limits;
}

public sealed class CatalogProjection(ProductRepository products, SearchGateway search)
{
    public ProductRepository Products { get; } = products;
    public SearchGateway Search { get; } = search;
}

public sealed class InventoryProjection(InventoryRepository inventory, InventoryThreshold threshold)
{
    public InventoryRepository Inventory { get; } = inventory;
    public InventoryThreshold Threshold { get; } = threshold;
}

public sealed class RevenueProjection(OrderRepository orders, PricingPolicy policy)
{
    public OrderRepository Orders { get; } = orders;
    public PricingPolicy Policy { get; } = policy;
}

public sealed class ShipmentProjection(ShipmentRepository shipments, ShipmentSla sla)
{
    public ShipmentRepository Shipments { get; } = shipments;
    public ShipmentSla Sla { get; } = sla;
}

// ===== Endpoints: 6 =====

public sealed class CheckoutEndpoint(PlaceOrderHandler place, ApplyPromotionHandler promotion, CartSummaryProjection summary)
{
    public PlaceOrderHandler Place { get; } = place;
    public ApplyPromotionHandler Promotion { get; } = promotion;
    public CartSummaryProjection Summary { get; } = summary;
}

public sealed class CartEndpoint(CartService cart, CartValidator validator, CartSummaryProjection summary)
{
    public CartService Cart { get; } = cart;
    public CartValidator Validator { get; } = validator;
    public CartSummaryProjection Summary { get; } = summary;
}

public sealed class CatalogEndpoint(CatalogService catalog, CatalogProjection projection, RecommendationGateway recommendations)
{
    public CatalogService Catalog { get; } = catalog;
    public CatalogProjection Projection { get; } = projection;
    public RecommendationGateway Recommendations { get; } = recommendations;
}

public sealed class OrderEndpoint(OrderSummaryProjection summary, CancelOrderHandler cancel, OrderService orders)
{
    public OrderSummaryProjection Summary { get; } = summary;
    public CancelOrderHandler Cancel { get; } = cancel;
    public OrderService Orders { get; } = orders;
}

public sealed class PaymentEndpoint(CapturePaymentHandler capture, RefundPaymentHandler refund, PaymentValidator validator)
{
    public CapturePaymentHandler Capture { get; } = capture;
    public RefundPaymentHandler Refund { get; } = refund;
    public PaymentValidator Validator { get; } = validator;
}

public sealed class ShipmentEndpoint(CreateShipmentHandler create, ShipmentProjection projection, ShipmentService shipments)
{
    public CreateShipmentHandler Create { get; } = create;
    public ShipmentProjection Projection { get; } = projection;
    public ShipmentService Shipments { get; } = shipments;
}
