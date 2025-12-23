using static Google.Cloud.Firestore.V1.StructuredQuery.Types;

namespace TheRockWasteManagement.Models
{
    public class WorkerAssignment
    {
        public int Id { get; set; }
        public int WorkerId { get; set; }
        public Worker Worker { get; set; }
        public int BookingId { get; set; }
        public Booking Booking { get; set; }
        public DateTime AssignmentDate { get; set; }
        public string Status { get; set; }
    }
}