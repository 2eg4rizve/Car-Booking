using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Globalization;
using Wafi.SampleTest.Dtos;
using Wafi.SampleTest.Entities;




namespace Wafi.SampleTest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BookingsController : ControllerBase
    {
        private readonly WafiDbContext _context;

        public BookingsController(WafiDbContext context)
        {
            _context = context;
        }

        // POST: api/Bookings
        [HttpPost("GetCalendarBookings")]
        public async Task<IActionResult> GetCalendarBookings([FromBody] BookingFilterDto input)
        {
            // Get booking from the database and filter the data
            //var bookings = await _context.Bookings.ToListAsync();

            // TO DO: convert the database bookings to calendar view (date, start time, end time). Consiser NoRepeat, Daily and Weekly options
            //return bookings;

            //throw new NotImplementedException();

            if (input == null)
            {
                return BadRequest("Invalid input data.");
            }

            if (input.StartBookingDate > input.EndBookingDate)
            {
                return BadRequest("Start date cannot be greater than end date.");
            }

            //if (input.CarId == Guid.Empty)
            //{
            //    return BadRequest("CarId is required.");
            //}

            

            try
            {
                var query = _context.Bookings.Include(b => b.Car).AsQueryable();

                
                if (input.CarId.HasValue)
                {
                    query = query.Where(b => b.CarId == input.CarId.Value);
                }

                var baseBookings = await query.ToListAsync();
                var expandedBookings = new List<BookingCalendarDto>();

                foreach (var booking in baseBookings)
                {
                    var currentDate = booking.BookingDate;
                    var endRepeatDate = booking.EndRepeatDate ?? input.EndBookingDate;

                    while (currentDate <= endRepeatDate && currentDate <= input.EndBookingDate)
                    {
                        if (currentDate >= input.StartBookingDate)
                        {
                            bool shouldInclude = false;

                            switch (booking.RepeatOption)
                            {
                                case RepeatOption.DoesNotRepeat:
                                    shouldInclude = (currentDate == booking.BookingDate);
                                    break;

                                case RepeatOption.Daily:
                                    shouldInclude = true;
                                    break;

                                case RepeatOption.Weekly:
                                    if (booking.DaysToRepeatOn.HasValue)
                                    {
                                        var currentDayFlag = (DaysOfWeek)(1 << ((int)currentDate.DayOfWeek));
                                        shouldInclude = (booking.DaysToRepeatOn.Value & currentDayFlag) != 0;
                                    }
                                    break;
                            }

                            if (shouldInclude)
                            {
                                expandedBookings.Add(new BookingCalendarDto
                                {
                                    Id = booking.Id,
                                    BookingDate = currentDate,
                                    StartTime = booking.StartTime,
                                    EndTime = booking.EndTime,
                                    RepeatOption = booking.RepeatOption,
                                    EndRepeatDate = booking.EndRepeatDate,
                                    DaysToRepeatOn = booking.DaysToRepeatOn,
                                    RequestedOn = booking.RequestedOn,
                                    CarId = booking.CarId,
                                    CarModel = $"{booking.Car?.Make} {booking.Car?.Model}",
                                    CarMake = booking.Car?.Make ?? "Unknown Make",
                                });
                            }
                        }

                        currentDate = currentDate.AddDays(1);
                    }
                }

                if (!expandedBookings.Any())
                {
                    return NotFound("No bookings found for the given date range.");
                }

                return Ok(expandedBookings);


            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }


        }

        // POST: api/Bookings
        [HttpPost("Booking")]
        public async Task<IActionResult> PostBooking([FromBody] CreateUpdateBookingDto bookingDto)
        {
            // TO DO: Validate if any booking time conflicts with existing data. Return error if any conflicts

            //await _context.Bookings.AddAsync(booking);
            //await _context.SaveChangesAsync();

            //return booking;
            // Convert DTO to Entity

            //throw new NotImplementedException();


            try
            {
                if (bookingDto == null)
                {
                    return BadRequest(new { message = "Invalid request. Booking data is required." });
                }

                var validationErrors = new List<string>();

                if (bookingDto.CarId == Guid.Empty)
                    validationErrors.Add("CarId is required.");
                if (bookingDto.BookingDate == default)
                    validationErrors.Add("BookingDate is required.");
                if (bookingDto.StartTime == default)
                    validationErrors.Add("StartTime is required.");
                if (bookingDto.EndTime == default)
                    validationErrors.Add("EndTime is required.");
                if (bookingDto.StartTime >= bookingDto.EndTime)
                    validationErrors.Add("StartTime must be earlier than EndTime.");
                if (bookingDto.RepeatOption != RepeatOption.DoesNotRepeat && bookingDto.EndRepeatDate == null)
                    validationErrors.Add("EndRepeatDate is required for recurring bookings.");

                if (validationErrors.Any())
                {
                    return BadRequest(new { message = "Validation failed.", errors = validationErrors });
                }

               
                if (bookingDto.Id != Guid.Empty)
                {
                    var existingBooking = await _context.Bookings.FindAsync(bookingDto.Id);
                    if (existingBooking == null)
                    {
                        return NotFound(new { message = "Booking not found." });
                    }

                   
                    bool conflictingBooking = await _context.Bookings
                        .Where(b => b.CarId == bookingDto.CarId &&
                                    b.Id != bookingDto.Id && 
                                    b.BookingDate == bookingDto.BookingDate &&
                                    ((b.StartTime < bookingDto.EndTime && b.EndTime > bookingDto.StartTime) ||
                                     (b.StartTime == bookingDto.StartTime && b.EndTime == bookingDto.EndTime)))
                        .AnyAsync();

                    if (conflictingBooking)
                    {
                        return BadRequest(new { message = "Booking conflict detected." });
                    }

                    
                    existingBooking.BookingDate = bookingDto.BookingDate;
                    existingBooking.StartTime = bookingDto.StartTime;
                    existingBooking.EndTime = bookingDto.EndTime;
                    existingBooking.RepeatOption = bookingDto.RepeatOption;
                    existingBooking.EndRepeatDate = bookingDto.EndRepeatDate;
                    existingBooking.DaysToRepeatOn = bookingDto.DaysToRepeatOn;
                    existingBooking.CarId = bookingDto.CarId;
                    existingBooking.RequestedOn = DateTime.UtcNow;

                    _context.Bookings.Update(existingBooking);
                    await _context.SaveChangesAsync();

                    return Ok(new { message = "Booking updated successfully.", bookingId = existingBooking.Id });
                }

                
                List<Booking> bookingsToAdd = new();
                DateOnly currentBookingDate = bookingDto.BookingDate;

                while (currentBookingDate <= bookingDto.EndRepeatDate)
                {
                    bool conflictingBooking = await _context.Bookings
                        .Where(b => b.CarId == bookingDto.CarId &&
                                    b.BookingDate == currentBookingDate &&
                                    ((b.StartTime < bookingDto.EndTime && b.EndTime > bookingDto.StartTime) ||
                                     (b.StartTime == bookingDto.StartTime && b.EndTime == bookingDto.EndTime)))
                        .AnyAsync();

                    if (conflictingBooking)
                    {
                        return BadRequest(new { message = $"Booking conflict on {currentBookingDate:yyyy-MM-dd}." });
                    }

                    bookingsToAdd.Add(new Booking
                    {
                        Id = Guid.NewGuid(),
                        BookingDate = currentBookingDate,
                        StartTime = bookingDto.StartTime,
                        EndTime = bookingDto.EndTime,
                        RepeatOption = bookingDto.RepeatOption,
                        EndRepeatDate = bookingDto.EndRepeatDate,
                        DaysToRepeatOn = bookingDto.DaysToRepeatOn,
                        CarId = bookingDto.CarId,
                        RequestedOn = DateTime.UtcNow
                    });

                    if (bookingDto.RepeatOption == RepeatOption.Daily)
                    {
                        currentBookingDate = currentBookingDate.AddDays(1);
                    }
                    else if (bookingDto.RepeatOption == RepeatOption.Weekly)
                    {
                        currentBookingDate = currentBookingDate.AddDays(7);
                    }
                    else
                    {
                        break;
                    }
                }

                await _context.Bookings.AddRangeAsync(bookingsToAdd);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    message = "Booking(s) created successfully.",
                    bookingIds = bookingsToAdd.Select(b => b.Id)
                });
            }
            catch (DbUpdateException dbEx)
            {
                return StatusCode(500, new { message = "Database error occurred while saving the booking.", error = dbEx.InnerException?.Message ?? dbEx.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "An unexpected error occurred.", error = ex.Message });
            }
        }

        // GET: api/SeedData
        // For test purpose
        [HttpGet("SeedData")]
        public async Task<IEnumerable<BookingCalendarDto>> GetSeedData()
        {
            var cars = await _context.Cars.ToListAsync();

            if (!cars.Any())
            {
                cars = GetCars().ToList();
                await _context.Cars.AddRangeAsync(cars);
                await _context.SaveChangesAsync();
            }

            var bookings = await _context.Bookings.ToListAsync();

            if(!bookings.Any())
            {
                bookings = GetBookings().ToList();

                await _context.Bookings.AddRangeAsync(bookings);
                await _context.SaveChangesAsync();
            }

            var calendar = new Dictionary<DateOnly, List<Booking>>();

            foreach (var booking in bookings)
            {
                var currentDate = booking.BookingDate;
                while (currentDate <= (booking.EndRepeatDate ?? booking.BookingDate))
                {
                    if (!calendar.ContainsKey(currentDate))
                        calendar[currentDate] = new List<Booking>();

                    calendar[currentDate].Add(booking);

                    currentDate = booking.RepeatOption switch
                    {
                        RepeatOption.Daily => currentDate.AddDays(1),
                        RepeatOption.Weekly => currentDate.AddDays(7),
                        _ => booking.EndRepeatDate.HasValue ? booking.EndRepeatDate.Value.AddDays(1) : currentDate.AddDays(1)
                    };
                }
            }

            List<BookingCalendarDto> result = new List<BookingCalendarDto>();

            foreach (var item in calendar)
            {
                foreach(var booking in item.Value)
                {
                    result.Add(new BookingCalendarDto { BookingDate = booking.BookingDate, CarModel = booking.Car.Model, StartTime = booking.StartTime, EndTime = booking.EndTime });
                }
            }

            return result;
        }

        #region Sample Data

        private IList<Car> GetCars()
        {
            var cars = new List<Car>
            {
                new Car { Id = Guid.NewGuid(), Make = "Toyota", Model = "Corolla" },
                new Car { Id = Guid.NewGuid(), Make = "Honda", Model = "Civic" },
                new Car { Id = Guid.NewGuid(), Make = "Ford", Model = "Focus" }
            };

            return cars;
        }

        private IList<Booking> GetBookings()
        {
            var cars = GetCars();

            var bookings = new List<Booking>
            {
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 5), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(12, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 10), StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(16, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 2, 20), RequestedOn = DateTime.Now, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 2, 15), StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 30, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 31), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Monday, CarId = cars[2].Id,  Car = cars[2] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 1), StartTime = new TimeSpan(11, 0, 0), EndTime = new TimeSpan(13, 0, 0), RepeatOption = RepeatOption.DoesNotRepeat, RequestedOn = DateTime.Now, CarId = cars[0].Id, Car = cars[0] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 7), StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(10, 0, 0), RepeatOption = RepeatOption.Weekly, EndRepeatDate = new DateOnly(2025, 3, 28), RequestedOn = DateTime.Now, DaysToRepeatOn = DaysOfWeek.Friday, CarId = cars[1].Id, Car = cars[1] },
                new Booking { Id = Guid.NewGuid(), BookingDate = new DateOnly(2025, 3, 15), StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(17, 0, 0), RepeatOption = RepeatOption.Daily, EndRepeatDate = new DateOnly(2025, 3, 20), RequestedOn = DateTime.Now, CarId = cars[2].Id,  Car = cars[2] }
            };

            return bookings;
        }

            #endregion

        }
}
