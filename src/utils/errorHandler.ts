export interface ApiErrorResponse {
  success?: boolean;
  message?: string;
  errors?: Record<string, string[] | string | unknown>;
}

export interface ErrorResult {
  type: 'validation' | 'business' | 'network' | 'server' | 'unknown';
  message: string;
  fieldErrors?: Record<string, string>;
}

export const parseApiError = (error: any): ErrorResult => {
  if (!error.response) {
    if (error.message) {
      return {
        type: 'network',
        message: error.message.includes('Network Error') 
          ? '网络连接失败，请检查网络' 
          : error.message
      };
    }
    return {
      type: 'network',
      message: '网络连接失败，请检查网络'
    };
  }

  const { data, status } = error.response;

  if (status >= 500) {
    const message = typeof data === 'string' 
      ? data 
      : (data?.message || data?.Message || '服务器内部错误');
    return {
      type: 'server',
      message: `服务器错误 (${status}): ${message}`
    };
  }

  const apiData = data as ApiErrorResponse;

  if (apiData.errors && Object.keys(apiData.errors).length > 0) {
    const fieldErrors: Record<string, string> = {};
    try {
      Object.entries(apiData.errors).forEach(([field, messages]) => {
        if (Array.isArray(messages)) {
          fieldErrors[field] = messages.join('；');
        } else if (typeof messages === 'string') {
          fieldErrors[field] = messages;
        } else if (messages && typeof messages === 'object' && messages.message) {
          fieldErrors[field] = String(messages.message);
        } else {
          fieldErrors[field] = String(messages);
        }
      });
    } catch (e) {
      console.error('Error parsing field errors:', e);
    }

    return {
      type: 'validation',
      message: apiData.message || apiData.Message || '数据校验失败',
      fieldErrors
    };
  }

  if (apiData.message || apiData.Message) {
    return {
      type: 'business',
      message: apiData.message || apiData.Message
    };
  }

  if (typeof data === 'string') {
    return {
      type: 'unknown',
      message: data
    };
  }

  if (data && typeof data === 'object') {
    const msg = data.error || data.Error || data.detail || data.Detail;
    if (msg) {
      return {
        type: 'unknown',
        message: String(msg)
      };
    }
  }

  return {
    type: 'unknown',
    message: `请求失败 (${status})，请稍后重试`
  };
};
