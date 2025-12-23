namespace TheRockWasteManagement.Models
{
    public class AssignWorkerViewModel
    {
        public string BookingId { get; set; }
        public string BookingAddress { get; set; }
        public string SelectedWorkerId { get; set; }
        public string SelectedBookingId { get; set; }

        public List<WorkerModel> Workers { get; set; } = new List<WorkerModel>();
        public List<BookingModel> Bookings { get; set; } = new List<BookingModel>();

        public DateTime BookingDate { get; set; }
        public string WorkerId { get; set; }
        public string WorkerName { get; set; }
        public string WorkerStatus { get; set; }

        // Additional properties for better functionality
        public string CustomerName { get; set; }
        public string TimeSlot { get; set; }
        public string CurrentStatus { get; set; }
    }
}