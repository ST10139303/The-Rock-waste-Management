namespace TheRockWasteManagement.Models
{
    public class AssignmentViewModel
    {
        public string Id { get; set; } // Assignment document ID
        public string WorkerId { get; set; }
        public string WorkerName { get; set; }
        public string WorkerEmail { get; set; }
        public string BookingId { get; set; }
        public string BookingAddress { get; set; }
        public DateTime BookingDate { get; set; }
        public string CustomerName { get; set; }
        public string WorkerStatus { get; set; } // Worker's current status
        public string BookingStatus { get; set; } // Booking's overall status
        public DateTime AssignedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string TimeSlot { get; set; }
        public string ServiceType { get; set; }
        public string Feedback { get; set; }
        public DateTime? FeedbackTimestamp { get; set; }

        // Status badge properties for UI
        public string WorkerStatusBadgeClass => WorkerStatus?.ToLower() switch
        {
            "pending" => "bg-warning text-dark",
            "in progress" => "bg-info text-dark",
            "attending" => "bg-primary text-white",
            "completed" => "bg-success text-white",
            "cancelled" => "bg-danger text-white",
            _ => "bg-secondary text-white"
        };

        public string BookingStatusBadgeClass => BookingStatus?.ToLower() switch
        {
            "assigned" => "bg-info text-white",
            "in progress" => "bg-primary text-white",
            "completed" => "bg-success text-white",
            "cancelled" => "bg-danger text-white",
            _ => "bg-secondary text-white"
        };

        public DateTime Date { get; internal set; }
    }
}