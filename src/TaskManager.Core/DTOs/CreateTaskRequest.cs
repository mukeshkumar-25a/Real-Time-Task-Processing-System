using System.ComponentModel.DataAnnotations;

namespace TaskManager.Core.DTOs;

public class CreateTaskRequest
{
    [Required]
    [MaxLength(100)]
    public string Type { get; set; } = string.Empty;
}
