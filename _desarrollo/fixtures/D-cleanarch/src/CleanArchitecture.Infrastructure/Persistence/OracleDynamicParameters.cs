using System.Data;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace CleanArchitecture.Infrastructure.Persistence;

/// <summary>
/// Parámetros de Dapper con soporte para REF CURSOR de Oracle, que
/// <see cref="DynamicParameters"/> no sabe declarar por sí solo.
/// </summary>
internal sealed class OracleDynamicParameters : SqlMapper.IDynamicParameters
{
    private readonly DynamicParameters _parameters = new();
    private readonly List<OracleParameter> _cursors = [];

    /// <summary>Agrega un parámetro de entrada normal.</summary>
    public void Add(string name, object? value) => _parameters.Add(name, value);

    /// <summary>Declara un parámetro OUT de tipo REF CURSOR.</summary>
    public void AddRefCursor(string name) =>
        _cursors.Add(new OracleParameter(name, OracleDbType.RefCursor, ParameterDirection.Output));

    void SqlMapper.IDynamicParameters.AddParameters(IDbCommand command, SqlMapper.Identity identity)
    {
        ((SqlMapper.IDynamicParameters)_parameters).AddParameters(command, identity);

        if (command is OracleCommand oracleCommand)
        {
            // Sin BindByName, ODP.NET enlaza por POSICIÓN y los nombres no importan.
            oracleCommand.BindByName = true;
            oracleCommand.Parameters.AddRange([.. _cursors]);
        }
    }
}
