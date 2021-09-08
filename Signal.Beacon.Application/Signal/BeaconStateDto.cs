using System.ComponentModel.DataAnnotations;

namespace Signal.Beacon.Application.Signal
{
    public class StationStateDto
    {
        [Required]
        public string? Id { get; set; }

        [Required]
        public string? Version { get; set; }
    }
}
