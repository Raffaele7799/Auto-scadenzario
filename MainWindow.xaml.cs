using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AutoScadenzario
{
    //====================================================================================
    // INIZIO: CLASSI DI SUPPORTO E MODELLI DATI
    // (Queste definizioni devono stare qui, all'interno del namespace 
    // ma FUORI dalla classe MainWindow)
    //====================================================================================

    public class DashboardDeadlineItem
    {
        public string VehicleName { get; set; } = string.Empty;
        public string DeadlineType { get; set; } = string.Empty;
        public DateTime ExpiryDate { get; set; }
        public int DaysRemaining { get; set; }
        public string DaysRemainingText => FormatDaysText(DaysRemaining);
        public string UrgencyColor => DaysRemaining < 0 ? "Red" : (DaysRemaining <= 30 ? "Orange" : "Green");

        private string FormatDaysText(int days)
        {
            if (days > 1) return $"Mancano {days} giorni";
            if (days == 1) return "Manca 1 giorno";
            if (days == 0) return "Scade Oggi";
            if (days == -1) return "Scaduta da 1 giorno";
            return $"Scaduta da {-days} giorni";
        }
    }

    public class DashboardAppointmentItem
    {
        public string VehicleName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime AppointmentDate { get; set; }
    }

    public enum DeadlineStatus { OK, Warning, Expired }

    public class MaintenanceRecord
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public string Description { get; set; } = string.Empty;
        public int Mileage { get; set; }
        public decimal Cost { get; set; }
    }

    public enum AppointmentStatus { Pianificato, Confermato, Completato, Annullato }
    public enum LinkedDeadlineType { Nessuno, Assicurazione, Revisione, Bollo }

    public class AppointmentRecord : INotifyPropertyChanged
    {
        private DateTime _date = DateTime.Today;
        private string _time = string.Empty;
        private string _description = string.Empty;
        private AppointmentStatus _status;
        private string _location = string.Empty;
        private LinkedDeadlineType _linkedDeadline;
        private decimal? _estimatedCost;
        private string _notes = string.Empty;

        public DateTime Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(nameof(Date)); }
        }

        public string Time
        {
            get => _time;
            set { _time = value; OnPropertyChanged(nameof(Time)); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(nameof(Description)); }
        }

        public AppointmentStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        public string Location
        {
            get => _location;
            set { _location = value; OnPropertyChanged(nameof(Location)); }
        }

        public LinkedDeadlineType LinkedDeadline
        {
            get => _linkedDeadline;
            set { _linkedDeadline = value; OnPropertyChanged(nameof(LinkedDeadline)); }
        }

        public decimal? EstimatedCost
        {
            get => _estimatedCost;
            set { _estimatedCost = value; OnPropertyChanged(nameof(EstimatedCost)); }
        }

        public string Notes
        {
            get => _notes;
            set { _notes = value; OnPropertyChanged(nameof(Notes)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Vehicle : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string LicensePlate { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        private string? _imagePath;

        public string? ImagePath
        {
            get => _imagePath;
            set { _imagePath = value; OnPropertyChanged(nameof(ImagePath)); }
        }

        public ObservableCollection<string> DocumentFolderPaths { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<MaintenanceRecord> MaintenanceHistory { get; set; } = new ObservableCollection<MaintenanceRecord>();
        public ObservableCollection<AppointmentRecord> AppointmentHistory { get; set; } = new ObservableCollection<AppointmentRecord>();

        [JsonIgnore]
        public BitmapImage? LoadedImage { get; private set; }

        public void LoadImageFromPath(string baseImagesPath)
        {
            if (string.IsNullOrEmpty(ImagePath))
            {
                LoadedImage = null;
            }
            else
            {
                string fullPath = Path.Combine(baseImagesPath, ImagePath);
                if (!File.Exists(fullPath))
                {
                    LoadedImage = null;
                }
                else
                {
                    try
                    {
                        BitmapImage bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fullPath);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        LoadedImage = bitmap;
                    }
                    catch
                    {
                        LoadedImage = null;
                    }
                }
            }
            OnPropertyChanged(nameof(LoadedImage));
        }

        private DateTime _insuranceExpiry;
        public DateTime InsuranceExpiry { get { return _insuranceExpiry; } set { _insuranceExpiry = value; OnPropertyChanged(nameof(InsuranceExpiry)); UpdateStatus(); } }
        private DateTime _revisionExpiry;
        public DateTime RevisionExpiry { get { return _revisionExpiry; } set { _revisionExpiry = value; OnPropertyChanged(nameof(RevisionExpiry)); UpdateStatus(); } }
        private DateTime _taxExpiry;
        public DateTime TaxExpiry { get { return _taxExpiry; } set { _taxExpiry = value; OnPropertyChanged(nameof(TaxExpiry)); UpdateStatus(); } }

        public DeadlineStatus InsuranceStatus { get; private set; }
        public string InsuranceDaysText { get; private set; } = string.Empty;
        public string InsuranceTooltip { get; private set; } = string.Empty;
        public DeadlineStatus RevisionStatus { get; private set; }
        public string RevisionDaysText { get; private set; } = string.Empty;
        public string RevisionTooltip { get; private set; } = string.Empty;
        public DeadlineStatus TaxStatus { get; private set; }
        public string TaxDaysText { get; private set; } = string.Empty;
        public string TaxTooltip { get; private set; } = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName)); }

        private string FormatDaysText(int days) { if (days > 1) return $"Mancano {days} Giorni"; if (days == 1) return "Manca 1 Giorno"; if (days == 0) return "Scade Oggi"; if (days == -1) return "Scaduto da 1 Giorno"; return $"Scaduto da {-days} Giorni"; }

        public void UpdateStatus() { var today = DateTime.Today; var warningDays = 30; var insuranceDays = (int)(InsuranceExpiry - today).TotalDays; InsuranceDaysText = FormatDaysText(insuranceDays); InsuranceTooltip = $"Scade il: {InsuranceExpiry:dd/MM/yyyy}"; if (insuranceDays < 0) { InsuranceStatus = DeadlineStatus.Expired; } else if (insuranceDays <= warningDays) { InsuranceStatus = DeadlineStatus.Warning; } else { InsuranceStatus = DeadlineStatus.OK; } var revisionDays = (int)(RevisionExpiry - today).TotalDays; RevisionDaysText = FormatDaysText(revisionDays); RevisionTooltip = $"Scade il: {RevisionExpiry:dd/MM/yyyy}"; if (revisionDays < 0) { RevisionStatus = DeadlineStatus.Expired; } else if (revisionDays <= warningDays) { RevisionStatus = DeadlineStatus.Warning; } else { RevisionStatus = DeadlineStatus.OK; } var taxDays = (int)(TaxExpiry - today).TotalDays; TaxDaysText = FormatDaysText(taxDays); TaxTooltip = $"Scade il: {TaxExpiry:dd/MM/yyyy}"; if (taxDays < 0) { TaxStatus = DeadlineStatus.Expired; } else if (taxDays <= warningDays) { TaxStatus = DeadlineStatus.Warning; } else { TaxStatus = DeadlineStatus.OK; } OnPropertyChanged(nameof(InsuranceStatus)); OnPropertyChanged(nameof(InsuranceDaysText)); OnPropertyChanged(nameof(InsuranceTooltip)); OnPropertyChanged(nameof(RevisionStatus)); OnPropertyChanged(nameof(RevisionDaysText)); OnPropertyChanged(nameof(RevisionTooltip)); OnPropertyChanged(nameof(TaxStatus)); OnPropertyChanged(nameof(TaxDaysText)); OnPropertyChanged(nameof(TaxTooltip)); }
    }

    //====================================================================================
    // FINE: CLASSI DI SUPPORTO E MODELLI DATI
    //====================================================================================


    //====================================================================================
    // INIZIO: CLASSE PRINCIPALE DELLA FINESTRA (MainWindow)
    //====================================================================================
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<Vehicle> Vehicles { get; set; }
        public ICollectionView VehiclesView { get; private set; }
        private string dataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoScadenzario", "data.json");
        private string _imagesFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoScadenzario", "Immagini");

        #region Dashboard Properties
        public ObservableCollection<DashboardDeadlineItem> UpcomingDeadlines { get; set; } = new ObservableCollection<DashboardDeadlineItem>();
        public ObservableCollection<DashboardAppointmentItem> UpcomingAppointments { get; set; } = new ObservableCollection<DashboardAppointmentItem>();

        private int _totalVehiclesCount;
        public int TotalVehiclesCount
        {
            get => _totalVehiclesCount;
            set { _totalVehiclesCount = value; OnPropertyChanged(nameof(TotalVehiclesCount)); }
        }

        public bool HasNoUpcomingDeadlines => UpcomingDeadlines.Count == 0;
        public bool HasNoUpcomingAppointments => UpcomingAppointments.Count == 0;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            QuestPDF.Settings.License = LicenseType.Community;
            Directory.CreateDirectory(_imagesFolderPath);

            LoadData();
            UpdateDashboard();

            VehiclesView = CollectionViewSource.GetDefaultView(Vehicles);
            VehiclesView.Filter = FilterVehicles;
            VehiclesDataGrid.ItemsSource = VehiclesView;
            DetailPanel.IsEnabled = false;
            AddPhotoButton.IsEnabled = false;
            this.Closing += MainWindow_Closing;
        }

        #region Dashboard Logic
        private void UpdateDashboard()
        {
            var today = DateTime.Today;
            TotalVehiclesCount = Vehicles.Count;
            UpcomingDeadlines.Clear();
            var allDeadlines = Vehicles.SelectMany(v => new[]
            {
                new DashboardDeadlineItem { VehicleName = v.Name, DeadlineType = "Assicurazione", ExpiryDate = v.InsuranceExpiry, DaysRemaining = (int)(v.InsuranceExpiry - today).TotalDays },
                new DashboardDeadlineItem { VehicleName = v.Name, DeadlineType = "Revisione", ExpiryDate = v.RevisionExpiry, DaysRemaining = (int)(v.RevisionExpiry - today).TotalDays },
                new DashboardDeadlineItem { VehicleName = v.Name, DeadlineType = "Bollo", ExpiryDate = v.TaxExpiry, DaysRemaining = (int)(v.TaxExpiry - today).TotalDays }
            });
            var sortedDeadlines = allDeadlines
                .Where(d => d.ExpiryDate >= today.AddDays(-15))
                .OrderBy(d => d.ExpiryDate)
                .Take(5);
            foreach (var deadline in sortedDeadlines)
            {
                UpcomingDeadlines.Add(deadline);
            }
            UpcomingAppointments.Clear();
            var allAppointments = Vehicles.SelectMany(v => v.AppointmentHistory.Select(a => new { Vehicle = v, Appointment = a }));
            var sortedAppointments = allAppointments
                .Where(item => item.Appointment.Date >= today && item.Appointment.Date <= today.AddDays(7))
                .OrderBy(item => item.Appointment.Date)
                .Take(5);
            foreach (var item in sortedAppointments)
            {
                if (DateTime.TryParse(item.Appointment.Time, out var timeOfDay))
                {
                    UpcomingAppointments.Add(new DashboardAppointmentItem
                    {
                        VehicleName = item.Vehicle.Name,
                        Description = item.Appointment.Description,
                        AppointmentDate = item.Appointment.Date.Add(timeOfDay.TimeOfDay)
                    });
                }
                else
                {
                    UpcomingAppointments.Add(new DashboardAppointmentItem
                    {
                        VehicleName = item.Vehicle.Name,
                        Description = item.Appointment.Description,
                        AppointmentDate = item.Appointment.Date
                    });
                }
            }
            OnPropertyChanged(nameof(HasNoUpcomingDeadlines));
            OnPropertyChanged(nameof(HasNoUpcomingAppointments));
        }
        #endregion

        #region INotifyPropertyChanged Implementation
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private bool FilterVehicles(object item)
        {
            if (string.IsNullOrEmpty(SearchTextBox.Text)) { return true; }
            else
            {
                var vehicle = item as Vehicle;
                if (vehicle == null) return false;
                string searchText = SearchTextBox.Text;
                return (vehicle.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (vehicle.LicensePlate?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (vehicle.Notes?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false);
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) { VehiclesView.Refresh(); }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e) { if (Vehicles.Any()) { VehiclesDataGrid.SelectedIndex = 0; } }

        private void VehiclesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Vehicle? selectedVehicle = VehiclesDataGrid.SelectedItem as Vehicle;
            AddPhotoButton.IsEnabled = (selectedVehicle != null);
            ClearMaintenanceForm();
            ClearAppointmentForm();

            if (selectedVehicle != null)
            {
                DetailPanel.IsEnabled = true;
                NameTextBox.Text = selectedVehicle.Name;
                LicensePlateTextBox.Text = selectedVehicle.LicensePlate;
                InsuranceDatePicker.SelectedDate = selectedVehicle.InsuranceExpiry;
                RevisionDatePicker.SelectedDate = selectedVehicle.RevisionExpiry;
                TaxDatePicker.SelectedDate = selectedVehicle.TaxExpiry;
                NotesTextBox.Text = selectedVehicle.Notes;
                VehicleImage.Source = selectedVehicle.LoadedImage;
                FolderPathsListBox.ItemsSource = selectedVehicle.DocumentFolderPaths;
                LoadDocumentsForSelectedVehicle();

                MaintenancePlaceholder.Visibility = Visibility.Collapsed;
                MaintenanceContentGrid.Visibility = Visibility.Visible;
                MaintenanceVehicleNameTitle.Text = selectedVehicle.Name;

                AppointmentPlaceholder.Visibility = Visibility.Collapsed;
                AppointmentContentGrid.Visibility = Visibility.Visible;
                AppointmentVehicleNameTitle.Text = selectedVehicle.Name;
            }
            else
            {
                DetailPanel.IsEnabled = false;
                ClearDetailForm();

                MaintenancePlaceholder.Visibility = Visibility.Visible;
                MaintenanceContentGrid.Visibility = Visibility.Collapsed;
                MaintenanceVehicleNameTitle.Text = "";

                AppointmentPlaceholder.Visibility = Visibility.Visible;
                AppointmentContentGrid.Visibility = Visibility.Collapsed;
                AppointmentVehicleNameTitle.Text = "";
            }
        }

        #region Manutenzione
        private void AddOrUpdateMaintenanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle)
            {
                if (string.IsNullOrWhiteSpace(MaintenanceDescriptionTextBox.Text)) { System.Windows.MessageBox.Show("La descrizione dell'intervento non può essere vuota.", "Dati Mancanti", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!int.TryParse(MaintenanceMileageTextBox.Text, out int mileage)) { System.Windows.MessageBox.Show("Il chilometraggio non è un numero valido.", "Dati Errati", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
                if (!decimal.TryParse(MaintenanceCostTextBox.Text, out decimal cost)) { System.Windows.MessageBox.Show("Il costo non è un numero valido.", "Dati Errati", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

                if (MaintenanceDataGrid.SelectedItem is MaintenanceRecord selectedRecord)
                {
                    selectedRecord.Date = MaintenanceDatePicker.SelectedDate ?? DateTime.Today;
                    selectedRecord.Description = MaintenanceDescriptionTextBox.Text;
                    selectedRecord.Mileage = mileage;
                    selectedRecord.Cost = cost;
                    MaintenanceDataGrid.Items.Refresh();
                    System.Windows.MessageBox.Show("Intervento modificato con successo!", "Salvataggio Manutenzione", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var newRecord = new MaintenanceRecord { Date = MaintenanceDatePicker.SelectedDate ?? DateTime.Today, Description = MaintenanceDescriptionTextBox.Text, Mileage = mileage, Cost = cost };
                    selectedVehicle.MaintenanceHistory.Add(newRecord);
                    System.Windows.MessageBox.Show("Nuovo intervento aggiunto con successo!", "Salvataggio Manutenzione", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                ClearMaintenanceForm();
            }
        }

        private void RemoveMaintenanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle && MaintenanceDataGrid.SelectedItem is MaintenanceRecord selectedRecord)
            {
                var result = System.Windows.MessageBox.Show($"Sei sicuro di voler eliminare l'intervento '{selectedRecord.Description}' del {selectedRecord.Date:dd/MM/yyyy}?", "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    selectedVehicle.MaintenanceHistory.Remove(selectedRecord);
                    ClearMaintenanceForm();
                }
            }
        }

        private void MaintenanceDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MaintenanceDataGrid.SelectedItem is MaintenanceRecord selectedRecord)
            {
                MaintenanceDatePicker.SelectedDate = selectedRecord.Date;
                MaintenanceDescriptionTextBox.Text = selectedRecord.Description;
                MaintenanceMileageTextBox.Text = selectedRecord.Mileage.ToString();
                MaintenanceCostTextBox.Text = selectedRecord.Cost.ToString();
                MaintenanceDetailDescription.Text = selectedRecord.Description;
                AddOrUpdateMaintenanceButton.Content = "Salva Modifiche";
                CancelMaintenanceEditButton.Visibility = Visibility.Visible;
                MaintenanceDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                ClearMaintenanceForm(clearSelection: false);
            }
        }

        private void ClearMaintenanceForm(bool clearSelection = true)
        {
            if (clearSelection && MaintenanceDataGrid.SelectedItem != null)
            {
                MaintenanceDataGrid.SelectedItem = null;
            }
            MaintenanceDatePicker.SelectedDate = DateTime.Today;
            MaintenanceDescriptionTextBox.Clear();
            MaintenanceMileageTextBox.Clear();
            MaintenanceCostTextBox.Clear();
            MaintenanceDetailDescription.Text = "";
            AddOrUpdateMaintenanceButton.Content = "Aggiungi Intervento";
            CancelMaintenanceEditButton.Visibility = Visibility.Collapsed;
            MaintenanceDetailPanel.Visibility = Visibility.Collapsed;
        }

        private void CancelMaintenanceEditButton_Click(object sender, RoutedEventArgs e)
        {
            ClearMaintenanceForm();
        }
        #endregion

        #region Appuntamenti
        private void AddOrUpdateAppointmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle)
            {
                if (string.IsNullOrWhiteSpace(AppointmentDescriptionTextBox.Text))
                {
                    System.Windows.MessageBox.Show("La descrizione dell'appuntamento non può essere vuota.", "Dati Mancanti", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                decimal? estimatedCost = null;
                if (!string.IsNullOrWhiteSpace(AppointmentCostTextBox.Text))
                {
                    if (decimal.TryParse(AppointmentCostTextBox.Text, out decimal cost))
                    {
                        estimatedCost = cost;
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Il costo previsto non è un numero valido.", "Dati Errati", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                if (AppointmentsDataGrid.SelectedItem is AppointmentRecord selectedAppointment)
                {
                    selectedAppointment.Date = AppointmentDatePicker.SelectedDate ?? DateTime.Today;
                    selectedAppointment.Time = AppointmentTimeTextBox.Text;
                    selectedAppointment.Description = AppointmentDescriptionTextBox.Text;
                    selectedAppointment.Location = AppointmentLocationTextBox.Text;
                    selectedAppointment.Status = (AppointmentStatus)AppointmentStatusComboBox.SelectedItem;
                    selectedAppointment.LinkedDeadline = (LinkedDeadlineType)AppointmentLinkedComboBox.SelectedItem;
                    selectedAppointment.EstimatedCost = estimatedCost;
                    selectedAppointment.Notes = AppointmentNotesTextBox.Text;
                    AppointmentsDataGrid.Items.Refresh();
                    System.Windows.MessageBox.Show("Appuntamento modificato con successo!", "Salvataggio Appuntamento", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var newAppointment = new AppointmentRecord
                    {
                        Date = AppointmentDatePicker.SelectedDate ?? DateTime.Today,
                        Time = AppointmentTimeTextBox.Text,
                        Description = AppointmentDescriptionTextBox.Text,
                        Location = AppointmentLocationTextBox.Text,
                        Status = (AppointmentStatus)AppointmentStatusComboBox.SelectedItem,
                        LinkedDeadline = (LinkedDeadlineType)AppointmentLinkedComboBox.SelectedItem,
                        EstimatedCost = estimatedCost,
                        Notes = AppointmentNotesTextBox.Text,
                    };
                    selectedVehicle.AppointmentHistory.Add(newAppointment);
                    System.Windows.MessageBox.Show("Nuovo appuntamento aggiunto con successo!", "Salvataggio Appuntamento", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                UpdateDashboard();

                if ((AppointmentStatus)AppointmentStatusComboBox.SelectedItem == AppointmentStatus.Completato)
                {
                    var result = System.Windows.MessageBox.Show(
                        "L'appuntamento è stato segnato come 'Completato'.\n\nVuoi creare una nuova registrazione di manutenzione basata su questo appuntamento?",
                        "Crea Manutenzione", MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        DetailPanel.SelectedIndex = 1;
                        ClearMaintenanceForm();
                        MaintenanceDatePicker.SelectedDate = AppointmentDatePicker.SelectedDate;
                        MaintenanceDescriptionTextBox.Text = AppointmentDescriptionTextBox.Text;
                        if (estimatedCost.HasValue) MaintenanceCostTextBox.Text = estimatedCost.Value.ToString();
                    }
                }

                ClearAppointmentForm();
            }
        }

        private void RemoveAppointmentButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle && AppointmentsDataGrid.SelectedItem is AppointmentRecord selectedAppointment)
            {
                var result = System.Windows.MessageBox.Show($"Sei sicuro di voler eliminare l'appuntamento '{selectedAppointment.Description}' del {selectedAppointment.Date:dd/MM/yyyy}?", "Conferma Eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    selectedVehicle.AppointmentHistory.Remove(selectedAppointment);
                    ClearAppointmentForm();
                    UpdateDashboard();
                }
            }
        }

        private void AppointmentsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AppointmentsDataGrid.SelectedItem is AppointmentRecord selectedAppointment)
            {
                AppointmentDatePicker.SelectedDate = selectedAppointment.Date;
                AppointmentTimeTextBox.Text = selectedAppointment.Time;
                AppointmentDescriptionTextBox.Text = selectedAppointment.Description;
                AppointmentLocationTextBox.Text = selectedAppointment.Location;
                AppointmentStatusComboBox.SelectedItem = selectedAppointment.Status;
                AppointmentLinkedComboBox.SelectedItem = selectedAppointment.LinkedDeadline;
                AppointmentCostTextBox.Text = selectedAppointment.EstimatedCost?.ToString();
                AppointmentNotesTextBox.Text = selectedAppointment.Notes;
                AddOrUpdateAppointmentButton.Content = "Salva Modifiche";
                CancelAppointmentEditButton.Visibility = Visibility.Visible;
            }
            else
            {
                ClearAppointmentForm(clearSelection: false);
            }
        }

        private void CancelAppointmentEditButton_Click(object sender, RoutedEventArgs e)
        {
            ClearAppointmentForm();
        }

        private void ClearAppointmentForm(bool clearSelection = true)
        {
            if (clearSelection && AppointmentsDataGrid.SelectedItem != null)
            {
                AppointmentsDataGrid.SelectedItem = null;
            }
            AppointmentDatePicker.SelectedDate = DateTime.Today;
            AppointmentTimeTextBox.Clear();
            AppointmentDescriptionTextBox.Clear();
            AppointmentLocationTextBox.Clear();
            AppointmentStatusComboBox.SelectedIndex = 0;
            AppointmentLinkedComboBox.SelectedIndex = 0;
            AppointmentCostTextBox.Clear();
            AppointmentNotesTextBox.Clear();
            AddOrUpdateAppointmentButton.Content = "Aggiungi Appuntamento";
            CancelAppointmentEditButton.Visibility = Visibility.Collapsed;
        }
        #endregion

        #region Funzioni Comuni e Dati
        private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Vehicles.Any()) { System.Windows.MessageBox.Show("Nessun veicolo da esportare.", "Lista Vuota", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog { Filter = "File PDF (*.pdf)|*.pdf", Title = "Salva Riepilogo Veicoli", FileName = $"Riepilogo_Veicoli_{DateTime.Now:yyyyMMdd}.pdf" };
            if (saveFileDialog.ShowDialog() == true)
            {
                string filePath = saveFileDialog.FileName;
                try
                {
                    Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.A4);
                            page.Margin(2, Unit.Centimetre);
                            page.PageColor(Colors.White);
                            page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Calibri"));
                            page.Header().AlignCenter().Text("Riepilogo Parco Veicoli - AutoScadenzario").SemiBold().FontSize(16).FontColor(Colors.Blue.Medium);
                            page.Content().PaddingVertical(1, Unit.Centimetre).Column(col =>
                            {
                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns => { columns.RelativeColumn(3); columns.RelativeColumn(2); columns.RelativeColumn(3); columns.RelativeColumn(3); columns.RelativeColumn(3); });
                                    table.Header(header => { header.Cell().Background("#4666FF").Padding(5).Text("Nome Veicolo").FontColor(Colors.White); header.Cell().Background("#4666FF").Padding(5).Text("Targa").FontColor(Colors.White); header.Cell().Background("#4666FF").Padding(5).Text("Assicurazione").FontColor(Colors.White); header.Cell().Background("#4666FF").Padding(5).Text("Revisione").FontColor(Colors.White); header.Cell().Background("#4666FF").Padding(5).Text("Bollo").FontColor(Colors.White); });
                                    foreach (var vehicle in Vehicles) { table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(vehicle.Name); table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(vehicle.LicensePlate); table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(vehicle.InsuranceDaysText); table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(vehicle.RevisionDaysText); table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(vehicle.TaxDaysText); }
                                });
                            });
                            page.Footer().AlignCenter().Text(x => { x.Span("Pagina "); x.CurrentPageNumber(); });
                        });
                    }).GeneratePdf(filePath);
                    MessageBoxResult result = System.Windows.MessageBox.Show("Esportazione completata con successo!\n\nVuoi aprire il file PDF adesso?", "Esportazione Riuscita", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes) { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
                }
                catch (Exception ex) { System.Windows.MessageBox.Show($"Si è verificato un errore durante l'esportazione del PDF:\n{ex.Message}", "Errore di Esportazione", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle && sender is DatePicker datePicker && datePicker.SelectedDate.HasValue)
            {
                switch (datePicker.Name)
                {
                    case "InsuranceDatePicker": selectedVehicle.InsuranceExpiry = datePicker.SelectedDate.Value; break;
                    case "RevisionDatePicker": selectedVehicle.RevisionExpiry = datePicker.SelectedDate.Value; break;
                    case "TaxDatePicker": selectedVehicle.TaxExpiry = datePicker.SelectedDate.Value; break;
                }
                UpdateDashboard();
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e) { SaveData(); }

        private void LoadData()
        {
            try
            {
                if (File.Exists(dataFilePath))
                {
                    string json = File.ReadAllText(dataFilePath);
                    var loadedVehicles = JsonConvert.DeserializeObject<ObservableCollection<Vehicle>>(json);
                    Vehicles = loadedVehicles ?? new ObservableCollection<Vehicle>();
                    foreach (var vehicle in Vehicles)
                    {
                        vehicle.UpdateStatus();
                        vehicle.LoadImageFromPath(_imagesFolderPath);
                    }
                }
                else
                {
                    Vehicles = new ObservableCollection<Vehicle>();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Errore durante il caricamento dei dati: {ex.Message}");
                Vehicles = new ObservableCollection<Vehicle>();
            }
        }

        private void SaveData() { try { Directory.CreateDirectory(Path.GetDirectoryName(dataFilePath)!); string json = JsonConvert.SerializeObject(Vehicles, Newtonsoft.Json.Formatting.Indented); File.WriteAllText(dataFilePath, json); } catch (Exception ex) { System.Windows.MessageBox.Show($"Errore durante il salvataggio dei dati: {ex.Message}"); } }

        private void AddPhotoButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle)
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "File immagine|*.jpg;*.jpeg;*.png;*.bmp|Tutti i file|*.*"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    string originalFilePath = openFileDialog.FileName;
                    try
                    {
                        string safeLicensePlate = string.Join("_", selectedVehicle.LicensePlate.Split(Path.GetInvalidFileNameChars()));
                        string newFileName = $"{safeLicensePlate}_{Path.GetFileName(originalFilePath)}";
                        string destinationPath = Path.Combine(_imagesFolderPath, newFileName);
                        File.Copy(originalFilePath, destinationPath, true);
                        selectedVehicle.ImagePath = newFileName;
                        selectedVehicle.LoadImageFromPath(_imagesFolderPath);
                        VehicleImage.Source = selectedVehicle.LoadedImage;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Impossibile salvare l'immagine:\n{ex.Message}", "Errore Salvataggio Immagine", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(NameTextBox.Text) || string.IsNullOrWhiteSpace(LicensePlateTextBox.Text))
            {
                System.Windows.MessageBox.Show("Nome veicolo e targa sono campi obbligatori.", "Dati mancanti", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Vehicle? selectedVehicle = VehiclesDataGrid.SelectedItem as Vehicle;
            if (selectedVehicle == null)
            {
                Vehicle newVehicle = new Vehicle { Name = NameTextBox.Text, LicensePlate = LicensePlateTextBox.Text, InsuranceExpiry = InsuranceDatePicker.SelectedDate ?? DateTime.Now.AddYears(1), RevisionExpiry = RevisionDatePicker.SelectedDate ?? DateTime.Now.AddYears(2), TaxExpiry = TaxDatePicker.SelectedDate ?? DateTime.Now.AddMonths(6), Notes = NotesTextBox.Text };
                newVehicle.UpdateStatus();
                Vehicles.Add(newVehicle);
                VehiclesDataGrid.SelectedItem = newVehicle;
                System.Windows.MessageBox.Show($"Il veicolo '{newVehicle.Name}' è stato creato con successo!", "Creazione Riuscita", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                selectedVehicle.Name = NameTextBox.Text;
                selectedVehicle.LicensePlate = LicensePlateTextBox.Text;
                selectedVehicle.InsuranceExpiry = InsuranceDatePicker.SelectedDate ?? selectedVehicle.InsuranceExpiry;
                selectedVehicle.RevisionExpiry = RevisionDatePicker.SelectedDate ?? selectedVehicle.RevisionExpiry;
                selectedVehicle.TaxExpiry = TaxDatePicker.SelectedDate ?? selectedVehicle.TaxExpiry;
                selectedVehicle.Notes = NotesTextBox.Text;
                System.Windows.MessageBox.Show($"Le modifiche al veicolo '{selectedVehicle.Name}' sono state salvate.", "Modifica Salvata", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            VehiclesView.Refresh();
            UpdateDashboard();
        }

        private void AddFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle)
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Seleziona una cartella da aggiungere"
                };
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (!selectedVehicle.DocumentFolderPaths.Contains(dialog.SelectedPath))
                    {
                        selectedVehicle.DocumentFolderPaths.Add(dialog.SelectedPath);
                        LoadDocumentsForSelectedVehicle();
                    }
                }
            }
        }

        private void RemoveFolderButton_Click(object sender, RoutedEventArgs e) { if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle && FolderPathsListBox.SelectedItem is string selectedPath) { selectedVehicle.DocumentFolderPaths.Remove(selectedPath); LoadDocumentsForSelectedVehicle(); } }
        private void ClearFoldersButton_Click(object sender, RoutedEventArgs e) { if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle) { var result = System.Windows.MessageBox.Show("Sei sicuro di voler scollegare tutte le cartelle per questo veicolo?", "Conferma Svuota", MessageBoxButton.YesNo, MessageBoxImage.Question); if (result == MessageBoxResult.Yes) { selectedVehicle.DocumentFolderPaths.Clear(); LoadDocumentsForSelectedVehicle(); } } }
        private void LoadDocumentsForSelectedVehicle()
        {
            DocumentsListBox.ItemsSource = null;
            if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle && selectedVehicle.DocumentFolderPaths.Any())
            {
                var allFiles = new List<FileInfo>();
                foreach (var path in selectedVehicle.DocumentFolderPaths)
                {
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            var filesInFolder = Directory.GetFiles(path).Select(f => new FileInfo(f));
                            allFiles.AddRange(filesInFolder);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.MessageBox.Show($"Impossibile leggere i file dalla cartella '{path}':\n{ex.Message}", "Errore Lettura Cartella", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
                DocumentsListBox.ItemsSource = allFiles.OrderBy(f => f.Name).ToList();
                DocumentsListBox.DisplayMemberPath = "Name";
            }
        }
        private void ClearDetailForm() { NameTextBox.Clear(); LicensePlateTextBox.Clear(); InsuranceDatePicker.SelectedDate = null; RevisionDatePicker.SelectedDate = null; TaxDatePicker.SelectedDate = null; VehicleImage.Source = null; NotesTextBox.Clear(); FolderPathsListBox.ItemsSource = null; DocumentsListBox.ItemsSource = null; }
        private void ClearNotesButton_Click(object sender, RoutedEventArgs e) { NotesTextBox.Clear(); }
        private void DeleteButton_Click(object sender, RoutedEventArgs e) { if (VehiclesDataGrid.SelectedItem is Vehicle selectedVehicle) { var result = System.Windows.MessageBox.Show($"Sei sicuro di voler eliminare '{selectedVehicle.Name}'?", "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Question); if (result == MessageBoxResult.Yes) { Vehicles.Remove(selectedVehicle); UpdateDashboard(); } } }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            VehiclesDataGrid.UnselectAll();
            ClearDetailForm();
            DetailPanel.IsEnabled = true;
            NameTextBox.Focus();
        }

        private void DocumentsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DocumentsListBox.SelectedItem is FileInfo selectedFile)
            {
                try
                {
                    var p = new Process { StartInfo = new ProcessStartInfo(selectedFile.FullName) { UseShellExecute = true } };
                    p.Start();
                }
                catch (Exception ex) { System.Windows.MessageBox.Show($"Impossibile aprire il file: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }
        #endregion
    }
}