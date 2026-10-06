using System.Diagnostics.CodeAnalysis;

namespace CleanArchitecture.Application.Abstractions;

/// <summary>Resultado de un caso de uso: éxito, o fallo con su <see cref="Error"/>.</summary>
public class Result
{
    // 📘 docs/07-casos-de-uso.md
    //
    // ¿Qué problema resuelve? Un caso de uso puede terminar bien o terminar en
    // un fallo ESPERADO ("no existe", "ya existe"). Las alternativas clásicas
    // son peores:
    //   - Devolver null: no explica POR QUÉ falló.
    //   - Lanzar una excepción: "no encontrado" no es excepcional, es un
    //     resultado normal; usar excepciones como control de flujo es caro y
    //     hace el código difícil de seguir.
    //
    // Con Result, el éxito o el fallo están EN LA FIRMA del método:
    //
    //     Task<Result<ProductDto>> ExecuteAsync(...)
    //
    // Quien llama VE que esto puede fallar y el compilador lo empuja a manejar
    // ambos caminos. Librerías como ErrorOr o CSharpFunctionalExtensions hacen
    // esto mismo con más funciones; escribirlo a mano una vez es la mejor forma
    // de entenderlas.

    protected Result(bool isSuccess, Error? error)
    {
        // Las dos invariantes del patrón: un éxito no lleva error y un fallo sí.
        // Hasta las clases de "fontanería" protegen sus reglas.
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("Un resultado exitoso no puede contener un error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("Un resultado fallido debe contener un error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>true si el caso de uso terminó bien.</summary>
    /// <remarks>
    /// [MemberNotNullWhen(false, ...)] le dice al compilador: "si esto es false,
    /// Error NO es null". Gracias a eso, quien comprueba IsSuccess puede usar
    /// Error sin el operador "!" y sin avisos de nulabilidad.
    /// </remarks>
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    /// <summary>true si el caso de uso falló. Se lee mejor que !IsSuccess.</summary>
    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    /// <summary>El fallo, o null si terminó bien.</summary>
    public Error? Error { get; }

    /// <summary>Éxito sin valor de retorno (por ejemplo, un borrado).</summary>
    public static Result Success() => new(true, null);

    /// <summary>Fallo sin valor de retorno.</summary>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Éxito con valor.</summary>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);

    /// <summary>Fallo en un caso de uso que devolvería un valor.</summary>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>Resultado de un caso de uso que devuelve un valor.</summary>
/// <typeparam name="TValue">Tipo del valor devuelto en caso de éxito.</typeparam>
public class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>
    /// El valor devuelto. Leerlo en un resultado fallido es un BUG de quien
    /// llama (debió comprobar IsSuccess antes), así que fallamos rápido y con
    /// un mensaje claro en lugar de devolver null y explotar más lejos.
    /// </summary>
    public TValue Value =>
        IsSuccess
            ? _value!
            : throw new InvalidOperationException(
                "No se puede leer Value de un resultado fallido. Comprueba IsSuccess primero.");

    /// <summary>
    /// Conversión implícita: permite escribir "return dto;" dentro de un caso
    /// de uso en lugar de "return Result.Success(dto);". Comodidad, no magia:
    /// el compilador llama a este método por ti.
    /// </summary>
    public static implicit operator Result<TValue>(TValue value) => Success(value);
}
