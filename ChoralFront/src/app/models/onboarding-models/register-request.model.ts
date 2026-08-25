// Reflète RegisterViewModel (back, Chorale.ViewModels.Auth). Corps de POST /api/auth/Register.
export interface IRegisterRequest {
  Firstname: string;
  Lastname: string;
  Email: string;
  Password: string;
}
