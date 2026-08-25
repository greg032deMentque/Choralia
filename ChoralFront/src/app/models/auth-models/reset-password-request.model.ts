export interface IResetPasswordRequest {
  UserId: string;
  Token: string;
  NewPassword: string;
}
