using pr7_shestakov.Models;

namespace pr7_shestakov.Services;

public interface IUserDialogService
{
    bool ConfirmDelete(Pet pet);
}
