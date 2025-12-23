// In BookingModel.cs
using Google.Cloud.Firestore;

namespace TheRockWasteManagement.Models
{
    public class BookingModel
    {
        
        public string BookingId { get; set; }
        public string WorkerStatus { get; set; }
        public string TimeSlot { get; set; }
        public string Address { get; set; }
        public string BookingAddress { get; set; }
        public DateTime BookingDate { get; set; }
        public string PreferredTime { get; set; }
        public string Status { get; set; }
        public string CustomerId { get; set; }
        public string CustomerName { get; set; }
        public string Id { get; set; }

        [FirestoreProperty]
        public string AssignedWorkerEmail { get; set; }

        [FirestoreProperty]
        public string WorkerFeedback { get; set; }

        [FirestoreProperty]
        public Timestamp FeedbackTimestamp { get; set; }

        [FirestoreProperty]
        public string AssignedWorker { get; set; }

        // NEW: Service Type and Pricing
        public string ServiceType { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal FinalPrice { get; set; }
        public bool IsPriceSet { get; set; }
        public string SpecialRequest { get; set; }
        public string PaymentStatus { get; set; } = "pending"; // pending, paid, failed
        public string AssignmentId { get; internal set; }
        public DateTime CompletedAt { get; internal set; }
    }
}