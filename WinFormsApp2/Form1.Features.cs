using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace WinFormsApp2
{
    // Second half of Form1 (same class, different file).
    // Contains: GDI+ seat map logic, Dashboard page (charts + bookings list),
    // and the new Windows Forms controls (DateTimePicker, NumericUpDown, ListView, ContextMenuStrip, ToolTip ...)
    public partial class Form1
    {
        // ---------- fields ----------
        private SeatMap seatMap;
        private Point? selectedPos;

        // bookings of each user (shown in the Dashboard)
        private readonly Dictionary<string, List<BookingInfo>> historyByUser =
            new Dictionary<string, List<BookingInfo>>(StringComparer.OrdinalIgnoreCase);

        // Dashboard page
        private SkyPanel pageStats;
        private BarChart barChart;
        private DonutChart donut;
        private Label lblDonutTitle, lblKpi;
        private ListView lvBookings;
        private ContextMenuStrip bookingsMenu;
        private string statsFlight;

        private List<BookingInfo> MyBookings()
        {
            string u = currentUser ?? "";
            List<BookingInfo> list;
            if (!historyByUser.TryGetValue(u, out list))
            {
                list = new List<BookingInfo>();
                historyByUser[u] = list;
            }
            return list;
        }

        // =====================================================
        //  Home page: new controls
        // =====================================================
        private DateTimePicker NewHomeDate(DateTime value)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                MinDate = DateTime.Today,
                Value = value,
                Font = new Font("Segoe UI Semibold", 12.5F)
            };
        }

        private NumericUpDown NewHomePax()
        {
            return new NumericUpDown
            {
                Minimum = 1,
                Maximum = 9,
                Value = 1,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = HomeInk,
                Font = new Font("Segoe UI Semibold", 12.5F)
            };
        }

        // Event handlers + tooltips of the home page (called at the end of BuildHomePage)
        private void WireHomeEvents()
        {
            // Return date can never be before the departure date
            txtRetDate.MinDate = txtDepDate.Value;
            txtDepDate.ValueChanged += (s, e) =>
            {
                if (txtRetDate.Value < txtDepDate.Value) txtRetDate.Value = txtDepDate.Value;
                txtRetDate.MinDate = txtDepDate.Value;
            };

            // "From" and "To" can not be the same city
            cmbFrom.SelectedIndexChanged += (s, e) =>
            {
                if (cmbFrom.SelectedIndex > 0 && cmbFrom.SelectedIndex == cmbTo.SelectedIndex)
                    cmbTo.SelectedIndex = 0;
            };
            cmbTo.SelectedIndexChanged += (s, e) =>
            {
                if (cmbTo.SelectedIndex > 0 && cmbFrom.SelectedIndex == cmbTo.SelectedIndex)
                    cmbFrom.SelectedIndex = 0;
            };

            var tip = new ToolTip { InitialDelay = 300 };
            tip.SetToolTip(cmbFrom, "Departure city (Any = all cities)");
            tip.SetToolTip(cmbTo, "Arrival city (Any = all cities)");
            tip.SetToolTip(txtDepDate, "Pick the departure date");
            tip.SetToolTip(txtRetDate, "Return date (not before departure)");
            tip.SetToolTip(txtPassengers, "Number of passengers (1-9)");
            tip.SetToolTip(btnSearch, "Show the available flights");
            tip.SetToolTip(navBlog, "Open the statistics dashboard");
        }

        // =====================================================
        //  Seat map (GDI+) logic - replaces the old Button grid
        // =====================================================
        private bool IsBooked(Point p)
        {
            return GetSeatState(currentFlight)[p.X, p.Y];
        }

        private void SeatMap_SeatClicked(Point p)
        {
            selectedPos = p;
            lblSelectedSeat.Text = string.Format("Selected seat: {0} ({1})",
                SeatMap.SeatName(p), IsBooked(p) ? "Booked" : "Available");
        }

        private void OnFlightChanged()
        {
            selectedPos = null;

            if (dgvFlights.CurrentRow == null)
            {
                currentFlight = null;
                seatMap.Visible = false;
                lblFlightTitle.Text = "No flights found";
                lblSelectedSeat.Text = "Selected seat: none";
                lblStatus.Text = "No flights available for this route.";
                return;
            }

            var row = dgvFlights.CurrentRow;
            currentFlight = row.Cells["FlightNo"].Value.ToString();
            lblFlightTitle.Text = string.Format("{0}:  {1}  \u2192  {2}",
                currentFlight, row.Cells["From"].Value, row.Cells["To"].Value);
            seatMap.Visible = true;
            seatMap.LoadSeats(GetSeatState(currentFlight));
            lblSelectedSeat.Text = "Selected seat: none";
            lblStatus.Text = "Flight " + currentFlight + " selected. Click a seat (or use the arrow keys).";
        }

        // "Book Seat" opens the passenger page. The seat becomes booked after the payment step.
        private void BookSeat()
        {
            if (!selectedPos.HasValue)
            {
                MessageBox.Show("Please select a seat first.", BrandName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (IsBooked(selectedPos.Value))
            {
                MessageBox.Show("Seat " + SeatMap.SeatName(selectedPos.Value) + " is already booked.",
                    BrandName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            OpenPassengerPage();
        }

        private void CancelSeat()
        {
            if (!selectedPos.HasValue)
            {
                MessageBox.Show("Please select a seat first.", BrandName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Point p = selectedPos.Value;
            string name = SeatMap.SeatName(p);
            if (!IsBooked(p))
            {
                MessageBox.Show("Seat " + name + " is not booked.", BrandName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GetSeatState(currentFlight)[p.X, p.Y] = false;
            string flight = currentFlight;
            MyBookings().RemoveAll(b => b.FlightNo == flight && b.SeatPos == p);   // keep the dashboard list in sync
            seatMap.Invalidate();
            lblSelectedSeat.Text = "Selected seat: " + name + " (Available)";
            lblStatus.Text = "Booking for seat " + name + " cancelled.";
        }

        // Called from "Book Seat": remembers flight + seat, then opens the passenger page
        private void OpenPassengerPage()
        {
            var row = dgvFlights.CurrentRow;
            Point pos = selectedPos.Value;
            pending = new BookingInfo
            {
                FlightNo = currentFlight,
                From = row.Cells["From"].Value.ToString(),
                To = row.Cells["To"].Value.ToString(),
                Time = row.Cells["Time"].Value.ToString(),
                Price = int.Parse(row.Cells["Price"].Value.ToString().TrimStart('$')),
                Date = txtDepDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                Seat = SeatMap.SeatName(pos),
                SeatPos = pos
            };

            lblPassFlight.Text = string.Format("{0}  \u00B7  {1} \u2192 {2}  \u00B7  Seat {3}  \u00B7  ${4}",
                pending.FlightNo, pending.From, pending.To, pending.Seat, pending.Price);
            lblPassError.Text = "";

            // Use the email typed on the home page if the field is still empty
            if (txtPassEmail.Text == "" && txtEmail.ForeColor == HomeInk)
                txtPassEmail.Text = txtEmail.Text.Trim();

            ShowPage(pagePassenger);
        }

        // The seat becomes "Booked" only here, after the payment step is done
        private void CompleteBooking()
        {
            pending.Reference = NewReference();

            GetSeatState(pending.FlightNo)[pending.SeatPos.X, pending.SeatPos.Y] = true;
            MyBookings().Add(pending);
            seatMap.Invalidate();
            lblSelectedSeat.Text = "Selected seat: " + pending.Seat + " (Booked)";
            lblStatus.Text = string.Format("Seat {0} booked successfully by {1}.", pending.Seat, currentUser);

            FillSummary(pending);
            ClearBookingForms();

            navStack.Clear();                  // booking finished: Back must not return to the payment page
            ShowPage(pageSummary, false);
        }

        // =====================================================
        //  Dashboard page (charts + bookings list)
        // =====================================================
        private Label PanelTitle(string text)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 13F),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };
        }

        private GlassPanel StatsPanel(Label title, Control body, Padding margin)
        {
            var panel = new GlassPanel
            {
                Dock = DockStyle.Fill,
                Radius = 28,
                Fill = Color.FromArgb(165, 24, 28, 44),
                Padding = new Padding(20, 14, 20, 16),
                Margin = margin
            };
            title.Dock = DockStyle.Top;
            title.Height = 36;
            body.Dock = DockStyle.Fill;
            panel.Controls.Add(body);    // Fill first
            panel.Controls.Add(title);   // then Top
            return panel;
        }

        private void BuildStatsPage()
        {
            pageStats = NewSky(1.5f);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(40, 114, 40, 32)
            };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            // Left: bar chart
            barChart = new BarChart();
            content.Controls.Add(StatsPanel(PanelTitle("Seat occupancy by flight"), barChart, new Padding(0, 0, 10, 0)), 0, 0);

            // Right: donut (top) + bookings list (bottom)
            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 2,
                Margin = new Padding(10, 0, 0, 0)
            };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            lblDonutTitle = PanelTitle("Seat status");
            donut = new DonutChart();
            right.Controls.Add(StatsPanel(lblDonutTitle, donut, new Padding(0, 0, 0, 8)), 0, 0);

            lblKpi = PanelTitle("My bookings");
            lvBookings = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                BackColor = Color.FromArgb(28, 32, 48),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10F)
            };
            lvBookings.Columns.Add("Ref", 90);
            lvBookings.Columns.Add("Passenger", 120);
            lvBookings.Columns.Add("Flight", 70);
            lvBookings.Columns.Add("Seat", 50);
            lvBookings.Columns.Add("Total", 60);
            lvBookings.SizeChanged += (s, e) =>
            {
                int w = lvBookings.ClientSize.Width;
                if (w <= 0) return;
                lvBookings.Columns[0].Width = (int)(w * 0.24);
                lvBookings.Columns[1].Width = (int)(w * 0.30);
                lvBookings.Columns[2].Width = (int)(w * 0.18);
                lvBookings.Columns[3].Width = (int)(w * 0.12);
                lvBookings.Columns[4].Width = (int)(w * 0.16);
            };

            // Events: selecting a booking updates the donut, right-click / Delete cancels it
            lvBookings.SelectedIndexChanged += (s, e) =>
            {
                if (lvBookings.SelectedItems.Count == 0) return;
                statsFlight = ((BookingInfo)lvBookings.SelectedItems[0].Tag).FlightNo;
                UpdateDonut();
            };
            lvBookings.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) CancelSelectedBooking();
            };
            bookingsMenu = new ContextMenuStrip();
            bookingsMenu.Items.Add("Cancel this booking").Click += (s, e) => CancelSelectedBooking();
            bookingsMenu.Opening += (s, e) => e.Cancel = lvBookings.SelectedItems.Count == 0;
            lvBookings.ContextMenuStrip = bookingsMenu;

            var hint = new Label
            {
                Text = "Right-click a booking to cancel it (or press Delete)",
                Dock = DockStyle.Bottom,
                Height = 24,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(150, 165, 195),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            lvBookings.Dock = DockStyle.Fill;
            var host = new Panel { BackColor = Color.Transparent };
            host.Controls.Add(lvBookings);
            host.Controls.Add(hint);
            right.Controls.Add(StatsPanel(lblKpi, host, new Padding(0, 8, 0, 0)), 0, 1);

            content.Controls.Add(right, 1, 0);
            pageStats.Controls.Add(content);

            MakeTopBar(pageStats, "Dashboard  \u00B7  Statistics", "Back", "\u2190", GoBack);
        }

        private void ShowDashboard()
        {
            if (currentUser == null) return;
            statsFlight = currentFlight;
            ShowPage(pageStats);
            RefreshStats();
        }

        private static int CountBooked(bool[,] st)
        {
            int n = 0;
            foreach (bool b in st) if (b) n++;
            return n;
        }

        private void RefreshStats()
        {
            // Bar chart: the flights of the last search (or a sample if nothing was searched yet)
            var nos = new List<string>();
            foreach (DataGridViewRow r in dgvFlights.Rows) nos.Add(Convert.ToString(r.Cells["FlightNo"].Value));
            if (nos.Count == 0) nos.AddRange(BuildFlights("Any", "Any").Select(f => f.No));
            nos = nos.Take(8).ToList();

            var vals = new List<float>();
            foreach (string no in nos)
                vals.Add(100f * CountBooked(GetSeatState(no)) / (Rows * 6));

            if (statsFlight == null) statsFlight = nos[0];
            barChart.Highlight = statsFlight;
            barChart.SetData(nos, vals);

            // Bookings list
            var mine = MyBookings();
            lvBookings.BeginUpdate();
            lvBookings.Items.Clear();
            int revenue = 0;
            foreach (var b in mine)
            {
                var item = new ListViewItem(new[] { b.Reference, b.FullName, b.FlightNo, b.Seat, "$" + b.Price }) { Tag = b };
                lvBookings.Items.Add(item);
                revenue += b.Price;
            }
            lvBookings.EndUpdate();
            lblKpi.Text = string.Format("My bookings  \u00B7  {0}  \u00B7  ${1}", mine.Count, revenue);

            UpdateDonut();
        }

        private void UpdateDonut()
        {
            if (statsFlight == null) return;
            donut.SetData(CountBooked(GetSeatState(statsFlight)), Rows * 6);
            lblDonutTitle.Text = "Seat status  \u00B7  " + statsFlight;
            barChart.Highlight = statsFlight;
            barChart.Invalidate();
        }

        private void CancelSelectedBooking()
        {
            if (lvBookings.SelectedItems.Count == 0) return;
            var b = (BookingInfo)lvBookings.SelectedItems[0].Tag;

            var answer = MessageBox.Show(
                string.Format("Cancel booking {0} (seat {1} on {2})?", b.Reference, b.Seat, b.FlightNo),
                BrandName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            GetSeatState(b.FlightNo)[b.SeatPos.X, b.SeatPos.Y] = false;
            MyBookings().Remove(b);
            if (seatMap != null) seatMap.Invalidate();
            statsFlight = b.FlightNo;
            RefreshStats();
        }
    }
}
