// Re-alias WPF types that conflict with System.Windows.Forms global usings
// introduced by <UseWindowsForms>true</UseWindowsForms>
global using Application = System.Windows.Application;
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Button = System.Windows.Controls.Button;
global using MessageBox = System.Windows.MessageBox;
global using ProgressBar = System.Windows.Controls.ProgressBar;
global using UserControl = System.Windows.Controls.UserControl;
