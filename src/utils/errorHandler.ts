export interface ApiErrorResponse {
  success: false;
  message: string;
  errors?: Record<string, string[]>;
}

export interface ErrorResult {
  type: 'validation' | 'business' | 'network' | 'unknown';
  message: string;
  fieldErrors?: Record<string, string>;
}

export const parseApiError = (error: any): ErrorResult => {
  if (!error.response) {
    return {
      type: 'network',
      message: '网络连接失败，请检查网络'
    };
  }

  const data = error.response.data as ApiErrorResponse;

  if (data.errors && Object.keys(data.errors).length > 0) {
    const fieldErrors: Record<string, string> = {};
    Object.entries(data.errors).forEach(([field, messages]) => {
      fieldErrors[field] = messages.join('；');
    });

    return {
      type: 'validation',
      message: data.message || '数据校验失败',
      fieldErrors
    };
  }

  if (data.message) {
    return {
      type: 'business',
      message: data.message
    };
  }

  return {
    type: 'unknown',
    message: '操作失败，请稍后重试'
  };
};
