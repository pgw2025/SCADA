export interface ApiErrorResponse {
  success?: boolean;
  message?: string;
  Message?: string;
  errors?: Record<string, string[] | string | Record<string, any> | unknown>;
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
    
    // 字段名称映射：后端字段名 -> 前端字段名
    const fieldNameMap: Record<string, string> = {
      'Code': 'Key',
      'Name': 'Name',
      'AreaId': 'AreaId',
      'ModelId': 'ModelId',
      'Rack': 'Rack',
      'Slot': 'Slot'
    };
    
    try {
      Object.entries(apiData.errors).forEach(([field, messages]) => {
        // 使用映射后的字段名
        const mappedField = fieldNameMap[field] || field;
        
        if (Array.isArray(messages)) {
          fieldErrors[mappedField] = messages.join('；');
        } else if (typeof messages === 'string') {
          fieldErrors[mappedField] = messages;
        } else if (messages && typeof messages === 'object' && 'message' in (messages as any)) {
          fieldErrors[mappedField] = String((messages as any).message);
        } else {
          fieldErrors[mappedField] = String(messages);
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
      message: apiData.message || apiData.Message || '业务异常'
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
