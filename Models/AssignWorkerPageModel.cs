namespace TheRockWasteManagement.Models
{
    public class AssignWorkerPageModel
    {
        public string BookingId { get; set; }
        public string WorkerId { get; set; }

        public List<WorkerModel> Workers { get; set; } = new List<WorkerModel>();
        public List<BookingModel> Bookings { get; set; } = new List<BookingModel>();
        public List<AssignmentViewModel> Assignments { get; set; } = new List<AssignmentViewModel>();

        public string SelectedWorkerId { get; set; }
        public string SelectedBookingId { get; set; }
        public DateTime BookingDate { get; set; }
        public string WorkerName { get; set; }
        public string WorkerStatus { get; set; }
        public string BookingAddress { get; set; }

        public string TimeSlot { get; set; }

        public List<AssignmentViewModel> CurrentAssignments { get; set; } = new List<AssignmentViewModel>();

        // Additional properties for better functionality
        public string CustomerName { get; set; }
        public string ServiceType { get; set; }
        public string AssignmentStatus { get; set; }
        public DateTime AssignmentDate { get; set; }
    }
}