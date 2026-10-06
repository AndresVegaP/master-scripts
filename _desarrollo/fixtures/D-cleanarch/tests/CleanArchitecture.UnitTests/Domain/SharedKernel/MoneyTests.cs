using CleanArchitecture.Domain.Common;
using CleanArchitecture.Domain.SharedKernel;

namespace CleanArchitecture.UnitTests.Domain.SharedKernel;

/// <summary>Tests del value object Money.</summary>
public class MoneyTests
{
    // 📘 docs/09-tests.md — CONVENCIONES DE ESTE REPO:
    //
    // 1. NOMBRE: Metodo_Escenario_ResultadoEsperado. Leer la lista de tests
    //    equivale a leer la especificación del sistema.
    // 2. ESTRUCTURA AAA: Arrange (preparar), Act (UNA acción), Assert (verificar).
    // 3. UN comportamiento por test. Si el nombre necesita una "y", son dos tests.
    // 4. [Theory] + [InlineData] para baterías de casos.

    [Fact]
    public void Create_ConDatosValidos_CreaElValueObject()
    {
        var money = Money.Create(10.50m, "USD");

        Assert.Equal(10.50m, money.Amount);
        Assert.Equal("USD", money.Currency.Code);
    }

    [Fact]
    public void Create_NormalizaLaEscalaSegunLaMoneda()
    {
        // 79990.0 y 79990 son el mismo número, pero se escriben distinto en JSON
        // y en la base de datos. Money deja siempre la forma canónica.
        var money = Money.Create(79990.0m, "CLP");

        Assert.Equal("79990 CLP", money.ToString());
    }

    [Fact]
    public void Create_ConImporteNegativo_LanzaDomainExceptionConSuCodigo()
    {
        var exception = Assert.Throws<DomainException>(() => Money.Create(-1m, "USD"));

        // Verificamos el CÓDIGO, no el texto: el mensaje puede cambiar sin que
        // eso sea un cambio de comportamiento.
        Assert.Equal("Money.NegativeAmount", exception.Code);
        Assert.Equal(DomainErrorKind.Invariant, exception.Kind);
    }

    [Theory]
    [InlineData(10.999, "USD")] // el dólar admite 2 decimales
    [InlineData(1990.55, "CLP")] // el peso chileno no admite decimales
    public void Create_ConDemasiadosDecimales_LanzaDomainException(decimal amount, string currency)
    {
        var exception = Assert.Throws<DomainException>(() => Money.Create(amount, currency));

        Assert.Equal("Money.TooManyDecimals", exception.Code);
    }

    [Fact]
    public void Create_ConDecimalesValidosParaLaMoneda_Funciona()
    {
        var money = Money.Create(10.99m, "USD");

        Assert.Equal(10.99m, money.Amount);
    }

    [Fact]
    public void Add_ConLaMismaMoneda_SumaYDevuelveOtroObjeto()
    {
        var a = Money.Create(1000m, "CLP");
        var b = Money.Create(500m, "CLP");

        var total = a.Add(b);

        Assert.Equal(1500m, total.Amount);
        Assert.Equal(1000m, a.Amount); // inmutabilidad: el original no cambió
    }

    [Fact]
    public void Add_ConMonedasDistintas_LanzaDomainException()
    {
        var pesos = Money.Create(1000m, "CLP");
        var dolares = Money.Create(10m, "USD");

        var exception = Assert.Throws<DomainException>(() => pesos.Add(dolares));

        Assert.Equal("Money.CurrencyMismatch", exception.Code);
        Assert.Equal(DomainErrorKind.Conflict, exception.Kind);
    }

    [Fact]
    public void Multiply_PorUnaCantidad_MultiplicaElImporte()
    {
        var precio = Money.Create(89990m, "CLP");

        var subtotal = precio.Multiply(3);

        Assert.Equal(269970m, subtotal.Amount);
    }

    [Fact]
    public void Multiply_PorCantidadNegativa_LanzaDomainException()
    {
        var exception = Assert.Throws<DomainException>(() => Money.Create(100m, "CLP").Multiply(-1));

        Assert.Equal("Money.NegativeQuantity", exception.Code);
    }

    [Fact]
    public void Zero_DevuelveCeroEnLaMonedaIndicada()
    {
        var zero = Money.Zero(Currency.Create("USD"));

        Assert.Equal(0m, zero.Amount);
        Assert.Equal("USD", zero.Currency.Code);
    }

    [Fact]
    public void Equals_MismoImporteYMoneda_SonIguales()
    {
        // Dos instancias distintas con el mismo valor SON iguales: esa es la
        // esencia de un value object.
        var a = Money.Create(1000m, "CLP");
        var b = Money.Create(1000m, "CLP");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_MismoImporteDistintaMoneda_NoSonIguales()
    {
        Assert.NotEqual(Money.Create(10m, "USD"), Money.Create(10m, "EUR"));
    }

    [Fact]
    public void ToString_MuestraElImporteConLosDecimalesDeLaMoneda()
    {
        Assert.Equal("10.50 USD", Money.Create(10.5m, "USD").ToString());
        Assert.Equal("89990 CLP", Money.Create(89990m, "CLP").ToString());
    }
}
