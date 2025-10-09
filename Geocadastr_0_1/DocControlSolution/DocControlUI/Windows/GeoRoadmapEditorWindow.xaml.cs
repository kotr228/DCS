using DocControlService.Client;
using DocControlService.Shared;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace DocControlUI.Windows
{
    public partial class GeoRoadmapEditorWindow : Window
    {
        private readonly DocControlServiceClient _client;
        private GeoRoadmap _currentRoadmap;
        private List<GeoRoadmapNode> _nodes;
        private List<GeoRoadmapRoute> _routes;
        private List<GeoRoadmapArea> _areas;
        private List<GeoRoadmapTemplate> _templates;

        private GeoRoadmapNode _selectedNode;
        private bool _isAddingNode = false;
        private bool _isConnectingNodes = false;
        private GeoRoadmapNode _connectFromNode = null;

        // Масштабування canvas (заглушка для справжньої карти)
        private double _canvasScale = 1.0;
        private Point _lastMousePosition;

        public GeoRoadmapEditorWindow(DocControlServiceClient client, int? roadmapId = null)
        {
            InitializeComponent();

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _nodes = new List<GeoRoadmapNode>();
            _routes = new List<GeoRoadmapRoute>();
            _areas = new List<GeoRoadmapArea>();

            Loaded += async (s, e) => await InitializeAsync(roadmapId);
        }

        #region Initialization

        private async System.Threading.Tasks.Task InitializeAsync(int? roadmapId)
        {
            try
            {
                SetStatus("Завантаження шаблонів...");
                _templates = await _client.GetGeoRoadmapTemplatesAsync();
                TemplateComboBox.ItemsSource = _templates;
                TemplateComboBox.DisplayMemberPath = "Name";

                if (roadmapId.HasValue)
                {
                    // Редагування існуючої карти
                    SetStatus("Завантаження геокарти...");
                    _currentRoadmap = await _client.GetGeoRoadmapByIdAsync(roadmapId.Value);
                    LoadRoadmap();
                }
                else
                {
                    // Створення нової карти
                    SetStatus("Створення нової геокарти...");
                    ShowNewRoadmapDialog();
                }

                SetStatus("Готово");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка ініціалізації: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }

        private void ShowNewRoadmapDialog()
        {
            var dialog = new NewGeoRoadmapDialog();
            if (dialog.ShowDialog() == true)
            {
                _currentRoadmap = new GeoRoadmap
                {
                    DirectoryId = dialog.SelectedDirectoryId,
                    Name = dialog.RoadmapName,
                    Description = dialog.RoadmapDescription,
                    MapProvider = MapProvider.OpenStreetMap,
                    CenterLatitude = 50.4501, // Київ за замовчуванням
                    CenterLongitude = 30.5234,
                    ZoomLevel = 10,
                    CreatedAt = DateTime.Now
                };

                LoadRoadmap();
            }
            else
            {
                Close();
            }
        }

        private void LoadRoadmap()
        {
            if (_currentRoadmap == null) return;

            RoadmapTitle.Text = $"📍 {_currentRoadmap.Name}";
            Title = $"Редактор геокарт - {_currentRoadmap.Name}";

            MapNameTextBox.Text = _currentRoadmap.Name;
            MapDescriptionTextBox.Text = _currentRoadmap.Description;
            CenterLatTextBox.Text = _currentRoadmap.CenterLatitude.ToString("F6");
            CenterLngTextBox.Text = _currentRoadmap.CenterLongitude.ToString("F6");
            ZoomSlider.Value = _currentRoadmap.ZoomLevel;

            _nodes = _currentRoadmap.Nodes ?? new List<GeoRoadmapNode>();
            _routes = _currentRoadmap.Routes ?? new List<GeoRoadmapRoute>();
            _areas = _currentRoadmap.Areas ?? new List<GeoRoadmapArea>();

            RefreshUI();
        }

        #endregion

        #region UI Refresh

        private void RefreshUI()
        {
            // Оновлюємо список вузлів
            NodesListBox.ItemsSource = null;
            NodesListBox.ItemsSource = _nodes;

            // Оновлюємо статистику
            NodeCountText.Text = _nodes.Count.ToString();
            RouteCountText.Text = _routes.Count.ToString();
            AreaCountText.Text = _areas.Count.ToString();

            // Розрахунок загальної відстані
            double totalDistance = CalculateTotalDistance();
            TotalDistanceText.Text = $"{totalDistance:F2} км";

            // Відображаємо на canvas
            RenderMap();
        }

        private void RenderMap()
        {
            MapCanvas.Children.Clear();

            // Відображаємо маршрути (лінії між вузлами)
            foreach (var route in _routes)
            {
                var fromNode = _nodes.FirstOrDefault(n => n.Id == route.FromNodeId);
                var toNode = _nodes.FirstOrDefault(n => n.Id == route.ToNodeId);

                if (fromNode != null && toNode != null)
                {
                    DrawRoute(fromNode, toNode, route);
                }
            }

            // Відображаємо вузли (точки)
            foreach (var node in _nodes)
            {
                DrawNode(node);
            }
        }

        private void DrawNode(GeoRoadmapNode node)
        {
            // Конвертуємо географічні координати в canvas координати (заглушка)
            var canvasPoint = GeoToCanvas(node.Latitude, node.Longitude);

            // Малюємо точку
            var ellipse = new Ellipse
            {
                Width = 20,
                Height = 20,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(node.Color ?? "#2196F3")),
                Stroke = Brushes.White,
                StrokeThickness = 3,
                Tag = node
            };

            Canvas.SetLeft(ellipse, canvasPoint.X - 10);
            Canvas.SetTop(ellipse, canvasPoint.Y - 10);

            ellipse.MouseLeftButtonDown += Node_MouseLeftButtonDown;
            ellipse.ToolTip = $"{node.Title}\n{node.Type}";

            MapCanvas.Children.Add(ellipse);

            // Додаємо підпис
            var label = new TextBlock
            {
                Text = node.Title,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
                Padding = new Thickness(3),
                Tag = node
            };

            Canvas.SetLeft(label, canvasPoint.X + 15);
            Canvas.SetTop(label, canvasPoint.Y - 10);

            MapCanvas.Children.Add(label);
        }

        private void DrawRoute(GeoRoadmapNode fromNode, GeoRoadmapNode toNode, GeoRoadmapRoute route)
        {
            var fromPoint = GeoToCanvas(fromNode.Latitude, fromNode.Longitude);
            var toPoint = GeoToCanvas(toNode.Latitude, toNode.Longitude);

            var line = new Line
            {
                X1 = fromPoint.X,
                Y1 = fromPoint.Y,
                X2 = toPoint.X,
                Y2 = toPoint.Y,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(route.Color ?? "#2196F3")),
                StrokeThickness = route.StrokeWidth,
                Tag = route
            };

            // Стиль лінії
            if (route.Style == RouteStyle.Dashed)
                line.StrokeDashArray = new DoubleCollection { 5, 3 };
            else if (route.Style == RouteStyle.Dotted)
                line.StrokeDashArray = new DoubleCollection { 2, 2 };

            MapCanvas.Children.Add(line);
        }

        // Конвертація географічних координат в canvas координати (заглушка)
        private Point GeoToCanvas(double lat, double lng)
        {
            // Проста лінійна проекція для заглушки
            // В реальній версії використовуватиметься Mercator projection

            double canvasWidth = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 800;
            double canvasHeight = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 600;

            // Центр карти
            double centerLat = _currentRoadmap?.CenterLatitude ?? 50.4501;
            double centerLng = _currentRoadmap?.CenterLongitude ?? 30.5234;

            // Масштаб
            double scale = Math.Pow(2, _currentRoadmap?.ZoomLevel ?? 10) * 2;

            double x = (lng - centerLng) * scale + canvasWidth / 2;
            double y = (centerLat - lat) * scale + canvasHeight / 2;

            return new Point(x, y);
        }

        // Зворотна конвертація canvas -> географічні координати
        private (double lat, double lng) CanvasToGeo(Point canvasPoint)
        {
            double canvasWidth = MapCanvas.ActualWidth > 0 ? MapCanvas.ActualWidth : 800;
            double canvasHeight = MapCanvas.ActualHeight > 0 ? MapCanvas.ActualHeight : 600;

            double centerLat = _currentRoadmap?.CenterLatitude ?? 50.4501;
            double centerLng = _currentRoadmap?.CenterLongitude ?? 30.5234;

            double scale = Math.Pow(2, _currentRoadmap?.ZoomLevel ?? 10) * 2;

            double lng = (canvasPoint.X - canvasWidth / 2) / scale + centerLng;
            double lat = centerLat - (canvasPoint.Y - canvasHeight / 2) / scale;

            return (lat, lng);
        }

        private double CalculateTotalDistance()
        {
            double total = 0;

            foreach (var route in _routes)
            {
                var fromNode = _nodes.FirstOrDefault(n => n.Id == route.FromNodeId);
                var toNode = _nodes.FirstOrDefault(n => n.Id == route.ToNodeId);

                if (fromNode != null && toNode != null)
                {
                    total += CalculateDistance(
                        fromNode.Latitude, fromNode.Longitude,
                        toNode.Latitude, toNode.Longitude);
                }
            }

            return total;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371; // Радіус Землі в км

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double degrees) => degrees * Math.PI / 180.0;

        #endregion

        #region Event Handlers

        private async void SaveRoadmap_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetStatus("Збереження геокарти...");

                // Оновлюємо дані з форми
                _currentRoadmap.Name = MapNameTextBox.Text;
                _currentRoadmap.Description = MapDescriptionTextBox.Text;
                _currentRoadmap.Nodes = _nodes;
                _currentRoadmap.Routes = _routes;
                _currentRoadmap.Areas = _areas;

                if (_currentRoadmap.Id == 0)
                {
                    // Створення нової
                    var request = new CreateGeoRoadmapRequest
                    {
                        DirectoryId = _currentRoadmap.DirectoryId,
                        Name = _currentRoadmap.Name,
                        Description = _currentRoadmap.Description,
                        MapProvider = _currentRoadmap.MapProvider,
                        CenterLatitude = _currentRoadmap.CenterLatitude,
                        CenterLongitude = _currentRoadmap.CenterLongitude,
                        ZoomLevel = _currentRoadmap.ZoomLevel
                    };

                    _currentRoadmap.Id = await _client.CreateGeoRoadmapAsync(request);

                    // Зберігаємо вузли
                    foreach (var node in _nodes)
                    {
                        node.GeoRoadmapId = _currentRoadmap.Id;
                        node.Id = await _client.AddGeoNodeAsync(node);
                    }

                    // Зберігаємо маршрути
                    foreach (var route in _routes)
                    {
                        route.GeoRoadmapId = _currentRoadmap.Id;
                        route.Id = await _client.AddGeoRouteAsync(route);
                    }

                    // Зберігаємо області
                    foreach (var area in _areas)
                    {
                        area.GeoRoadmapId = _currentRoadmap.Id;
                        area.Id = await _client.AddGeoAreaAsync(area);
                    }
                }
                else
                {
                    // Оновлення існуючої
                    await _client.UpdateGeoRoadmapAsync(_currentRoadmap);
                }

                SetStatus("Геокарту збережено");
                MessageBox.Show("Геокарту успішно збережено!", "Успіх",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка збереження: {ex.Message}", "Помилка",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddNode_Click(object sender, RoutedEventArgs e)
        {
            _isAddingNode = true;
            _isConnectingNodes = false;
            MapModeText.Text = "Режим: Додавання точки (клікніть на карті)";
            SetStatus("Клікніть на карті для додавання точки");
        }

        private void ConnectNodes_Click(object sender, RoutedEventArgs e)
        {
            _isConnectingNodes = true;
            _isAddingNode = false;
            _connectFromNode = null;
            MapModeText.Text = "Режим: З'єднання точок (виберіть дві точки)";
            SetStatus("Виберіть першу точку для з'єднання");
        }

        private void AddArea_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функція додавання областей буде реалізована в v0.4", "Інформація",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isAddingNode)
            {
                var clickPoint = e.GetPosition(MapCanvas);
                var geoCoords = CanvasToGeo(clickPoint);

                var dialog = new NodeEditDialog(null);
                if (dialog.ShowDialog() == true)
                {
                    var newNode = new GeoRoadmapNode
                    {
                        GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                        Title = dialog.NodeTitle,
                        Description = dialog.NodeDescription,
                        Latitude = geoCoords.lat,
                        Longitude = geoCoords.lng,
                        Type = dialog.SelectedNodeType,
                        Color = dialog.SelectedColor,
                        OrderIndex = _nodes.Count
                    };

                    _nodes.Add(newNode);
                    RefreshUI();

                    _isAddingNode = false;
                    MapModeText.Text = "Режим: Перегляд";
                    SetStatus($"Додано точку: {newNode.Title}");
                }
            }
        }

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse ellipse && ellipse.Tag is GeoRoadmapNode node)
            {
                if (_isConnectingNodes)
                {
                    if (_connectFromNode == null)
                    {
                        _connectFromNode = node;
                        SetStatus($"Перша точка: {node.Title}. Виберіть другу точку.");
                    }
                    else
                    {
                        // Створюємо маршрут
                        var route = new GeoRoadmapRoute
                        {
                            GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                            FromNodeId = _connectFromNode.Id != 0 ? _connectFromNode.Id : _nodes.IndexOf(_connectFromNode) + 1,
                            ToNodeId = node.Id != 0 ? node.Id : _nodes.IndexOf(node) + 1,
                            Color = "#2196F3",
                            Style = RouteStyle.Solid,
                            StrokeWidth = 2
                        };

                        _routes.Add(route);
                        RefreshUI();

                        SetStatus($"Створено маршрут: {_connectFromNode.Title} → {node.Title}");
                        _connectFromNode = null;
                        _isConnectingNodes = false;
                        MapModeText.Text = "Режим: Перегляд";
                    }
                }
                else
                {
                    // Вибір вузла
                    _selectedNode = node;
                    NodesListBox.SelectedItem = node;
                    ShowNodeProperties(node);
                }

                e.Handled = true;
            }
        }

        private void Node_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (NodesListBox.SelectedItem is GeoRoadmapNode node)
            {
                _selectedNode = node;
                ShowNodeProperties(node);
            }
        }

        private void ShowNodeProperties(GeoRoadmapNode node)
        {
            NodePropertiesGroup.Visibility = Visibility.Visible;
            NodeTitleTextBox.Text = node.Title;
            NodeDescriptionTextBox.Text = node.Description;
            NodeTypeComboBox.Text = node.Type.ToString();
            NodeLatTextBox.Text = node.Latitude.ToString("F6");
            NodeLngTextBox.Text = node.Longitude.ToString("F6");
            NodeAddressTextBox.Text = node.Address;

            // Вибираємо колір
            foreach (ComboBoxItem item in NodeColorComboBox.Items)
            {
                if (item.Tag?.ToString() == node.Color)
                {
                    NodeColorComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void AddNodeFromPanel_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new NodeEditDialog(null);
            if (dialog.ShowDialog() == true)
            {
                var newNode = new GeoRoadmapNode
                {
                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                    Title = dialog.NodeTitle,
                    Description = dialog.NodeDescription,
                    Latitude = _currentRoadmap?.CenterLatitude ?? 50.4501,
                    Longitude = _currentRoadmap?.CenterLongitude ?? 30.5234,
                    Type = dialog.SelectedNodeType,
                    Color = dialog.SelectedColor,
                    OrderIndex = _nodes.Count
                };

                _nodes.Add(newNode);
                RefreshUI();
                SetStatus($"Додано точку: {newNode.Title}");
            }
        }

        private void EditNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null)
            {
                MessageBox.Show("Виберіть точку для редагування", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new NodeEditDialog(_selectedNode);
            if (dialog.ShowDialog() == true)
            {
                _selectedNode.Title = dialog.NodeTitle;
                _selectedNode.Description = dialog.NodeDescription;
                _selectedNode.Type = dialog.SelectedNodeType;
                _selectedNode.Color = dialog.SelectedColor;

                RefreshUI();
                SetStatus($"Оновлено точку: {_selectedNode.Title}");
            }
        }

        private void DeleteNode_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null)
            {
                MessageBox.Show("Виберіть точку для видалення", "Увага",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Видалити точку '{_selectedNode.Title}'?\n\nБудуть також видалені всі пов'язані маршрути.",
                "Підтвердження",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Видаляємо пов'язані маршрути
                _routes.RemoveAll(r => r.FromNodeId == _selectedNode.Id || r.ToNodeId == _selectedNode.Id);

                _nodes.Remove(_selectedNode);
                _selectedNode = null;
                NodePropertiesGroup.Visibility = Visibility.Collapsed;

                RefreshUI();
                SetStatus("Точку видалено");
            }
        }

        private async void Geocode_Click(object sender, RoutedEventArgs e)
        {
            var address = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть адресу для пошуку:",
                "Геокодування",
                "");

            if (!string.IsNullOrWhiteSpace(address))
            {
                try
                {
                    SetStatus("Пошук адреси...");
                    var result = await _client.GeocodeAddressAsync(address);

                    if (result.Success)
                    {
                        MessageBox.Show(
                            $"Знайдено:\n\n{result.FormattedAddress}\n\nLat: {result.Latitude:F6}\nLng: {result.Longitude:F6}",
                            "Результат геокодування",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        // Пропонуємо додати точку
                        var addResult = MessageBox.Show(
                            "Додати точку на цю адресу?",
                            "Додати точку",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);

                        if (addResult == MessageBoxResult.Yes)
                        {
                            var dialog = new NodeEditDialog(null);
                            if (dialog.ShowDialog() == true)
                            {
                                var newNode = new GeoRoadmapNode
                                {
                                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                                    Title = dialog.NodeTitle,
                                    Description = dialog.NodeDescription,
                                    Latitude = result.Latitude,
                                    Longitude = result.Longitude,
                                    Address = result.FormattedAddress,
                                    Type = dialog.SelectedNodeType,
                                    Color = dialog.SelectedColor,
                                    OrderIndex = _nodes.Count
                                };

                                _nodes.Add(newNode);
                                RefreshUI();
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show("Адресу не знайдено", "Помилка",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    SetStatus("Готово");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка геокодування: {ex.Message}", "Помилка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Template_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Обробка зміни шаблону
        }

        private void ApplyTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateComboBox.SelectedItem is GeoRoadmapTemplate template)
            {
                var result = MessageBox.Show(
                    $"Застосувати шаблон '{template.Name}'?\n\nПоточні дані будуть замінені.",
                    "Підтвердження",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // TODO: Реалізувати застосування шаблону
                    MessageBox.Show("Функція застосування шаблонів буде повністю реалізована в v0.4",
                        "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void MapProvider_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Зміна провайдера карти (для v0.4)
        }

        private void Zoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_currentRoadmap != null)
            {
                _currentRoadmap.ZoomLevel = (int)e.NewValue;
                RefreshUI();
            }
        }

        private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var point = e.GetPosition(MapCanvas);
            var geoCoords = CanvasToGeo(point);
            CoordinatesText.Text = $"Lat: {geoCoords.lat:F6}, Lng: {geoCoords.lng:F6}";
        }

        private void MapCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Зум колесом миші
            if (_currentRoadmap != null)
            {
                int delta = e.Delta > 0 ? 1 : -1;
                int newZoom = Math.Max(1, Math.Min(18, _currentRoadmap.ZoomLevel + delta));
                ZoomSlider.Value = newZoom;
            }
        }

        private void CenterMap_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count > 0)
            {
                // Розраховуємо центр для всіх точок
                double avgLat = _nodes.Average(n => n.Latitude);
                double avgLng = _nodes.Average(n => n.Longitude);

                _currentRoadmap.CenterLatitude = avgLat;
                _currentRoadmap.CenterLongitude = avgLng;

                CenterLatTextBox.Text = avgLat.ToString("F6");
                CenterLngTextBox.Text = avgLng.ToString("F6");

                RefreshUI();
                SetStatus("Карту відцентровано");
            }
        }

        private void MeasureDistance_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count >= 2)
            {
                var distance = CalculateTotalDistance();
                MessageBox.Show(
                    $"Загальна відстань маршруту: {distance:F2} км",
                    "Вимірювання відстані",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Додайте принаймні 2 точки для вимірювання відстані",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void FindNodeOnMap_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedNode != null)
            {
                _currentRoadmap.CenterLatitude = _selectedNode.Latitude;
                _currentRoadmap.CenterLongitude = _selectedNode.Longitude;
                _currentRoadmap.ZoomLevel = 14;

                CenterLatTextBox.Text = _selectedNode.Latitude.ToString("F6");
                CenterLngTextBox.Text = _selectedNode.Longitude.ToString("F6");
                ZoomSlider.Value = 14;

                RefreshUI();
                SetStatus($"Відцентровано на: {_selectedNode.Title}");
            }
        }

        private void OptimizeRoute_Click(object sender, RoutedEventArgs e)
        {
            if (_nodes.Count < 3)
            {
                MessageBox.Show("Для оптимізації потрібно принаймні 3 точки",
                    "Увага", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Простий алгоритм найближчого сусіда
            var optimized = new List<GeoRoadmapNode> { _nodes[0] };
            var remaining = new List<GeoRoadmapNode>(_nodes.Skip(1));

            while (remaining.Count > 0)
            {
                var current = optimized.Last();
                GeoRoadmapNode nearest = null;
                double minDist = double.MaxValue;

                foreach (var node in remaining)
                {
                    var dist = CalculateDistance(
                        current.Latitude, current.Longitude,
                        node.Latitude, node.Longitude);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearest = node;
                    }
                }

                if (nearest != null)
                {
                    optimized.Add(nearest);
                    remaining.Remove(nearest);
                }
            }

            // Оновлюємо порядок
            for (int i = 0; i < optimized.Count; i++)
            {
                optimized[i].OrderIndex = i;
            }

            _nodes = optimized;

            // Перебудовуємо маршрути
            _routes.Clear();
            for (int i = 0; i < _nodes.Count - 1; i++)
            {
                _routes.Add(new GeoRoadmapRoute
                {
                    GeoRoadmapId = _currentRoadmap?.Id ?? 0,
                    FromNodeId = i + 1,
                    ToNodeId = i + 2,
                    Color = "#2196F3",
                    Style = RouteStyle.Solid,
                    StrokeWidth = 2
                });
            }

            RefreshUI();
            MessageBox.Show("Маршрут оптимізовано!", "Успіх",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddMilestonesFromFiles_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функція буде реалізована в v0.4\n\nБуде автоматично створювати віхи на основі дат файлів з директорії",
                "Інформація", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void SaveAsTemplate_Click(object sender, RoutedEventArgs e)
        {
            var name = Microsoft.VisualBasic.Interaction.InputBox(
                "Введіть назву шаблону:",
                "Зберегти як шаблон",
                "");

            if (!string.IsNullOrWhiteSpace(name))
            {
                try
                {
                    await _client.SaveAsTemplateAsync(
                        _currentRoadmap.Id,
                        name,
                        "Користувацький шаблон",
                        "Користувацькі");

                    MessageBox.Show("Шаблон збережено!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Оновлюємо список шаблонів
                    _templates = await _client.GetGeoRoadmapTemplatesAsync();
                    TemplateComboBox.ItemsSource = _templates;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка збереження шаблону: {ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"georoadmap_{_currentRoadmap?.Name}_{DateTime.Now:yyyyMMdd}.json"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var json = System.Text.Json.JsonSerializer.Serialize(_currentRoadmap, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    System.IO.File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Геокарту експортовано!", "Успіх",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Помилка експорту: {ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Зберегти зміни перед закриттям?",
                "Підтвердження",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                SaveRoadmap_Click(sender, e);
                Close();
            }
            else if (result == MessageBoxResult.No)
            {
                Close();
            }
        }

        #endregion

        #region Helper Methods

        private void SetStatus(string message)
        {
            StatusText.Text = $"{DateTime.Now:HH:mm:ss} - {message}";
        }

        #endregion
    }

    #region Helper Dialogs

    /// <summary>
    /// Діалог створення нової геокарти
    /// </summary>
    public class NewGeoRoadmapDialog : Window
    {
        private TextBox nameTextBox;
        private TextBox descriptionTextBox;
        private ComboBox directoryComboBox;

        public string RoadmapName => nameTextBox.Text;
        public string RoadmapDescription => descriptionTextBox.Text;
        public int SelectedDirectoryId => directoryComboBox.SelectedIndex + 1;

        public NewGeoRoadmapDialog()
        {
            Title = "Нова геодорожня карта";
            Width = 500;
            Height = 300;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Назва
            var nameLabel = new TextBlock { Text = "Назва карти:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(nameLabel, 0);
            grid.Children.Add(nameLabel);

            nameTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(nameTextBox, 0);
            grid.Children.Add(nameTextBox);

            // Опис
            var descLabel = new TextBlock { Text = "Опис:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(descLabel, 1);
            grid.Children.Add(descLabel);

            descriptionTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10), Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true };
            Grid.SetRow(descriptionTextBox, 1);
            grid.Children.Add(descriptionTextBox);

            // Директорія (заглушка)
            var dirLabel = new TextBlock { Text = "Директорія:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(dirLabel, 2);
            grid.Children.Add(dirLabel);

            directoryComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            directoryComboBox.Items.Add("Директорія 1");
            directoryComboBox.SelectedIndex = 0;
            Grid.SetRow(directoryComboBox, 2);
            grid.Children.Add(directoryComboBox);

            // Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 4);

            var okButton = new Button { Content = "Створити", Width = 100, Margin = new Thickness(5), IsDefault = true };
            okButton.Click += (s, e) => { DialogResult = true; Close(); };

            var cancelButton = new Button { Content = "Скасувати", Width = 100, Margin = new Thickness(5), IsCancel = true };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    /// <summary>
    /// Діалог редагування вузла
    /// </summary>
    public class NodeEditDialog : Window
    {
        private TextBox titleTextBox;
        private TextBox descriptionTextBox;
        private ComboBox typeComboBox;
        private ComboBox colorComboBox;

        public string NodeTitle => titleTextBox.Text;
        public string NodeDescription => descriptionTextBox.Text;
        public NodeType SelectedNodeType => Enum.Parse<NodeType>(typeComboBox.Text);
        public string SelectedColor => (colorComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#2196F3";

        public NodeEditDialog(GeoRoadmapNode existingNode)
        {
            Title = existingNode == null ? "Нова точка" : "Редагування точки";
            Width = 400;
            Height = 350;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Назва
            var titleLabel = new TextBlock { Text = "Назва точки:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(titleLabel, 0);
            grid.Children.Add(titleLabel);

            titleTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10), Text = existingNode?.Title ?? "" };
            Grid.SetRow(titleTextBox, 0);
            grid.Children.Add(titleTextBox);

            // Опис
            var descLabel = new TextBlock { Text = "Опис:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(descLabel, 1);
            grid.Children.Add(descLabel);

            descriptionTextBox = new TextBox { Margin = new Thickness(0, 0, 0, 10), Height = 60, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Text = existingNode?.Description ?? "" };
            Grid.SetRow(descriptionTextBox, 1);
            grid.Children.Add(descriptionTextBox);

            // Тип
            var typeLabel = new TextBlock { Text = "Тип:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(typeLabel, 2);
            grid.Children.Add(typeLabel);

            typeComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var type in Enum.GetValues(typeof(NodeType)))
            {
                typeComboBox.Items.Add(type.ToString());
            }
            typeComboBox.SelectedIndex = 0;
            if (existingNode != null)
                typeComboBox.Text = existingNode.Type.ToString();
            Grid.SetRow(typeComboBox, 2);
            grid.Children.Add(typeComboBox);

            // Колір
            var colorLabel = new TextBlock { Text = "Колір:", Margin = new Thickness(0, 5, 0, 5) };
            Grid.SetRow(colorLabel, 3);
            grid.Children.Add(colorLabel);

            colorComboBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🔵 Синій", Tag = "#2196F3" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🔴 Червоний", Tag = "#F44336" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟢 Зелений", Tag = "#4CAF50" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟡 Жовтий", Tag = "#FFEB3B" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟣 Фіолетовий", Tag = "#9C27B0" });
            colorComboBox.Items.Add(new ComboBoxItem { Content = "🟠 Помаранчевий", Tag = "#FF9800" });
            colorComboBox.SelectedIndex = 0;
            Grid.SetRow(colorComboBox, 3);
            grid.Children.Add(colorComboBox);

            // Кнопки
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };
            Grid.SetRow(buttonPanel, 5);

            var okButton = new Button { Content = "OK", Width = 100, Margin = new Thickness(5), IsDefault = true };
            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(titleTextBox.Text))
                {
                    MessageBox.Show("Введіть назву точки", "Увага", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            };

            var cancelButton = new Button { Content = "Скасувати", Width = 100, Margin = new Thickness(5), IsCancel = true };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    #endregion
}
