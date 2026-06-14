using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StrategyHouse.Domain.Entities;
using StrategyHouse.Infrastructure.Persistence;

namespace StrategyHouse.Web.Services;

// Generates department access codes, avoiding visually confusing characters (0/O/1/I).
public class AccessCodeService
{
    private const string Chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly ApplicationDbContext _db;

    public AccessCodeService(ApplicationDbContext db) { _db = db; }

    public static string GenerateCode(int length = 6)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.Select(b => Chars[b % Chars.Length]).ToArray());
    }

    // Creates a unique active code for the department, regenerating on collision.
    public async Task<DepartmentAccessCode> CreateForDepartmentAsync(string deptCode, string? createdByUserId)
    {
        string code;
        do
        {
            code = GenerateCode();
        }
        while (await _db.DepartmentAccessCodes.AnyAsync(c => c.Code == code));

        var entity = new DepartmentAccessCode
        {
            Code = code,
            DeptCode = deptCode,
            IsActive = true,
            CreatedByUserId = createdByUserId,
        };
        _db.DepartmentAccessCodes.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }
}
