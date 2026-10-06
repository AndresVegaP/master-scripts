using CleanArchitecture.Domain.Orders;
using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.UnitTests;

/// <summary>Fábrica de datos de prueba: objetos válidos listos para usar.</summary>
internal static class Some
{
    // 📘 docs/09-tests.md
    //
    // Esto es un "Object Mother" (o test data builder): un lugar único donde se
    // arma el objeto válido típico, con parámetros opcionales para cambiar SOLO
    // lo que le importa a cada test.
    //
    //     var product = Some.Product(stock: 0);   // se lee: "un producto sin stock"
    //
    // Ventajas: los tests dicen QUÉ es importante en cada caso (lo que aparece
    // entre paréntesis) y, si mañana el constructor cambia, se arregla aquí y no
    // en cincuenta tests.

    /// <summary>Momento fijo para los tests. El tiempo congelado hace que los resultados sean siempre iguales.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Un producto válido del catálogo.</summary>
    public static Product Product(
        string sku = "TEC-0001",
        string name = "Teclado mecánico",
        decimal price = 89990m,
        string currency = "CLP",
        int stock = 10,
        DateTimeOffset? now = null) =>
        // "global::" evita que el compilador confunda CleanArchitecture.Domain
        // (la capa) con CleanArchitecture.UnitTests.Domain (la carpeta de tests).
        global::CleanArchitecture.Domain.Products.Product.Create(
            Sku.Create(sku),
            ProductName.Create(name),
            global::CleanArchitecture.Domain.SharedKernel.Money.Create(price, currency),
            stock,
            now ?? Now);

    /// <summary>Un importe en la moneda indicada.</summary>
    public static Money Money(decimal amount = 1000m, string currency = "CLP") =>
        global::CleanArchitecture.Domain.SharedKernel.Money.Create(amount, currency);

    /// <summary>Un pedido en borrador, sin líneas.</summary>
    public static Order DraftOrder(string currency = "CLP", DateTimeOffset? now = null) =>
        Order.Create(Currency.Create(currency), now ?? Now);

    /// <summary>Un pedido ya confirmado con una línea del producto indicado.</summary>
    public static Order PlacedOrder(ProductId productId, int quantity = 2, decimal unitPrice = 89990m, string currency = "CLP")
    {
        var order = DraftOrder(currency);
        order.AddLine(productId, Money(unitPrice, currency), quantity);
        order.Place(Now);

        return order;
    }
}
