namespace TheRockWasteManagement.Models
{
    // Create this model in your Models folder
    public class PaymentModel
    {
        public string Id { get; set; }
        public string CustomerName { get; set; }
        public string Description { get; set; }
        public double Amount { get; set; }
        public string PaymentMethod { get; set; }
        public string Reference { get; set; }
        public DateTime PaymentDate { get; set; }
        public string Status { get; set; }
        public string BookingId { get; set; }

        public string CustomerId { get; set; } // Add this if missing
    }
}