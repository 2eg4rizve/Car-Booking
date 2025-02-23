using System.ComponentModel.DataAnnotations;
using Wafi.SampleTest.Entities;

namespace Wafi.SampleTest.Dtos
{
    public class BookingCalendarDto
    {
        public Guid Id { get; set; }

        public DateOnly BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }



        public RepeatOption RepeatOption { get; set; }

        public DateOnly? EndRepeatDate { get; set; }

        public DaysOfWeek? DaysToRepeatOn { get; set; }



        public DateTime RequestedOn { get; set; }


        public Guid CarId { get; set; }
        public string CarModel { get; set; }
        public string CarMake { get; set; }
    }
}
