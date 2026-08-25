export interface IApiResponse<T> {
  Data: T;
  Message?: string;
}

export interface IApiErrorResponse {
  StatusCode?: number;
  TraceId?: string;
  Message?: string;
  Errors?: string[];
}
