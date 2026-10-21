using System.Windows;
using pr7_shestakov.Models;

namespace pr7_shestakov.Services;

public sealed class MessageBoxDialogService : IUserDialogService
{
    public bool ConfirmDelete(Pet pet) => MessageBox.Show(
        $"Удалить питомца «{pet.Name}» (ID {pet.Id})?",
        "Удаление записи", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No)
        == MessageBoxResult.Yes;
}
