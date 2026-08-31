using Freebuff.Platform.Shared.Models;

namespace Freebuff.Platform.Application.Interfaces;

public interface ICrudService<TDto, TCreateDto, TUpdateDto, TFilterDto>
    where TDto : class
    where TCreateDto : class
    where TUpdateDto : class
    where TFilterDto : PagedRequest
{
    Task<TDto?> GetByIdAsync(Guid id);
    Task<PagedResult<TDto>> GetListAsync(TFilterDto filter);
    Task<TDto> CreateAsync(TCreateDto dto, string userId);
    Task<TDto?> UpdateAsync(Guid id, TUpdateDto dto, string userId);
    Task<bool> SoftDeleteAsync(Guid id, string userId, string? reason = null);
    Task<bool> RestoreAsync(Guid id, string userId);
    Task<List<Freebuff.Platform.Application.DTOs.AuditEntryDto>> GetAuditHistoryAsync(Guid entityId);
}
