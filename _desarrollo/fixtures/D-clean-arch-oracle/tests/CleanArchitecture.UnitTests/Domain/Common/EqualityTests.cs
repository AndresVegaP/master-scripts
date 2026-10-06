using CleanArchitecture.Domain.Products;
using CleanArchitecture.Domain.SharedKernel;
using CleanArchitecture.UnitTests.Fakes;

namespace CleanArchitecture.UnitTests.Domain.Common;

/// <summary>Tests de las clases base Entity y ValueObject.</summary>
public class EqualityTests
{
    // Estas dos clases base son "fontanería" del dominio, pero si su igualdad
    // está mal, fallan cosas muy difíciles de depurar (un HashSet con productos
    // duplicados, un Find que no encuentra lo que está ahí). Por eso se prueban.

    [Fact]
    public void Entidades_ConElMismoId_SonIguales()
    {
        var original = Some.Product();

        // Rehidratar el mismo producto crea otro objeto en memoria...
        var rehydrated = Product.FromSnapshot(original.ToSnapshot());

        // ...pero es LA MISMA entidad, porque tiene la misma identidad.
        Assert.Equal(original, rehydrated);
        Assert.True(original == rehydrated);
        Assert.Equal(original.GetHashCode(), rehydrated.GetHashCode());
    }

    [Fact]
    public void Entidades_ConDistintoId_NoSonIguales()
    {
        Assert.NotEqual(Some.Product(sku: "AAA-0001"), Some.Product(sku: "AAA-0001"));
    }

    [Fact]
    public void Entidades_FuncionanComoClaveEnColecciones()
    {
        var product = Some.Product();
        var set = new HashSet<Product> { product, Product.FromSnapshot(product.ToSnapshot()) };

        // El mismo producto dos veces sigue siendo UN elemento.
        Assert.Single(set);
    }

    [Fact]
    public void ValueObjects_ConLosMismosComponentes_SonIguales()
    {
        Assert.Equal(Money.Create(1000m, "CLP"), Money.Create(1000m, "CLP"));
    }

    [Fact]
    public void ValueObjects_DeTiposDistintos_NoSonIguales()
    {
        // Aunque por dentro ambos guarden el mismo texto, un SKU no es un nombre.
        Assert.NotEqual<object>(Sku.Create("ABC-0001"), ProductName.Create("ABC-0001"));
    }

    [Fact]
    public void ValueObjects_ComparadosConNull_NoSonIguales()
    {
        var money = Money.Create(1m, "USD");

        Assert.False(money.Equals(null));
        Assert.False(money == null);
        Assert.True(money != null);
    }

    [Fact]
    public void IdsFuertementeTipados_NoSeMezclan()
    {
        // Este test documenta algo que en realidad garantiza el COMPILADOR:
        // ProductId y OrderId no son intercambiables aunque ambos envuelvan un
        // Guid. Si alguien cambiara los tipos por Guid, este archivo seguiría
        // compilando y el error aparecería en producción.
        var productId = ProductId.New();
        var sameValue = new ProductId(productId.Value);

        Assert.Equal(productId, sameValue);
        Assert.NotEqual(productId, ProductId.New());
    }
}
