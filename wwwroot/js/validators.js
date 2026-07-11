/**
 * 公共安全工具 - 成绩管理系统
 * 统一 XSS 防护、错误处理、数据校验
 */

// ========== XSS 防护 ==========

/**
 * HTML 实体编码 - 所有用户数据注入 DOM 前必须调用
 * 使用 textContent 方式，避免任何 HTML 注入
 */
function safeHtml(str) {
    if (str === null || str === undefined) return '';
    var text = String(str);
    var div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

/**
 * 安全属性值编码 - 用于 HTML 属性中插入用户数据
 */
function safeAttr(str) {
    if (str === null || str === undefined) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

/**
 * 简单 HTML 净化 - 移除危险标签和属性
 * 用于富文本场景（如公告内容）
 */
function sanitizeHtml(html) {
    if (!html) return '';
    var text = String(html);
    // 移除 script/iframe/object/embed 标签
    text = text.replace(/<script\b[^<]*(?:(?!<\/script>)<[^<]*)*<\/script>/gi, '');
    text = text.replace(/<iframe\b[^<]*(?:(?!<\/iframe>)<[^<]*)*<\/iframe>/gi, '');
    text = text.replace(/<object\b[^<]*(?:(?!<\/object>)<[^<]*)*<\/object>/gi, '');
    text = text.replace(/<embed\b[^>]*\/?>/gi, '');
    // 移除 on* 事件属性
    text = text.replace(/\son\w+\s*=\s*("[^"]*"|'[^']*'|[^\s>]*)/gi, '');
    // 移除 javascript: 协议
    text = text.replace(/href\s*=\s*"javascript:[^"]*"/gi, 'href="#"');
    text = text.replace(/href\s*=\s*'javascript:[^']*'/gi, "href='#'");
    return text;
}

// ========== 统一错误处理 ==========

/**
 * 错误类型枚举
 */
var ErrorType = {
    NETWORK: 'network',
    TIMEOUT: 'timeout',
    UNAUTHORIZED: 'unauthorized',
    FORBIDDEN: 'forbidden',
    NOT_FOUND: 'not_found',
    SERVER_ERROR: 'server_error',
    VALIDATION: 'validation',
    UNKNOWN: 'unknown'
};

/**
 * 解析 HTTP 错误为结构化信息
 */
function parseError(xhr, textStatus, errorThrown) {
    var result = {
        type: ErrorType.UNKNOWN,
        title: '未知错误',
        message: '操作失败，请稍后重试',
        action: '请检查网络连接后重试',
        status: 0
    };

    if (textStatus === 'timeout' || errorThrown === 'Timeout') {
        return {
            type: ErrorType.TIMEOUT,
            title: '请求超时',
            message: '服务器响应时间过长',
            action: '请稍后重试；若持续超时，请联系管理员检查服务器状态',
            status: 0
        };
    }

    if (textStatus === 'error' && !xhr.status) {
        return {
            type: ErrorType.NETWORK,
            title: '网络连接失败',
            message: '无法连接到服务器',
            action: '请检查：1) 网络是否正常  2) 服务器是否在运行  3) 防火墙是否拦截',
            status: 0
        };
    }

    result.status = xhr.status;

    // 尝试从响应体获取服务器错误信息
    var serverMsg = '';
    try {
        var resp = JSON.parse(xhr.responseText);
        if (resp.message) serverMsg = resp.message;
    } catch (e) { /* 非 JSON 响应 */ }

    switch (xhr.status) {
        case 400:
            return {
                type: ErrorType.VALIDATION,
                title: '数据校验失败',
                message: serverMsg || '提交的数据不符合要求',
                action: '请检查输入数据是否正确，修正后重试',
                status: 400
            };
        case 401:
            return {
                type: ErrorType.UNAUTHORIZED,
                title: '登录已过期',
                message: '您的登录凭证已失效',
                action: '请刷新页面重新登录',
                status: 401
            };
        case 403:
            return {
                type: ErrorType.FORBIDDEN,
                title: '权限不足',
                message: serverMsg || '您没有执行此操作的权限',
                action: '请联系管理员申请相应权限',
                status: 403
            };
        case 404:
            return {
                type: ErrorType.NOT_FOUND,
                title: '请求的资源不存在',
                message: serverMsg || '目标页面或数据不存在',
                action: '请检查操作是否正确，或联系管理员',
                status: 404
            };
        case 405:
            return {
                type: ErrorType.FORBIDDEN,
                title: '请求方法不允许',
                message: '服务器拒绝此操作方式',
                action: '请刷新页面后重试',
                status: 405
            };
        case 429:
            return {
                type: ErrorType.FORBIDDEN,
                title: '请求过于频繁',
                message: '您的操作频率过高',
                action: '请等待片刻后再试',
                status: 429
            };
        case 500:
        case 502:
        case 503:
            return {
                type: ErrorType.SERVER_ERROR,
                title: '服务器错误 (' + xhr.status + ')',
                message: serverMsg || '服务器内部出现错误',
                action: '请稍后重试；若持续出现，请联系管理员',
                status: xhr.status
            };
        default:
            if (xhr.status >= 500) {
                return {
                    type: ErrorType.SERVER_ERROR,
                    title: '服务器错误 (' + xhr.status + ')',
                    message: serverMsg || '服务器处理请求时出错',
                    action: '请稍后重试',
                    status: xhr.status
                };
            }
            return {
                type: ErrorType.UNKNOWN,
                title: '请求失败 (' + xhr.status + ')',
                message: serverMsg || xhr.responseText || '未知错误',
                action: '请重试或联系管理员',
                status: xhr.status
            };
    }
}

/**
 * 显示错误提示（Bootstrap Toast 风格）
 */
function showError(error) {
    // 构建错误消息
    var msg = '【' + error.title + '】\n' + error.message;
    if (error.action) {
        msg += '\n\n💡 ' + error.action;
    }
    alert(msg);
}

/**
 * 统一 AJAX 错误处理
 * @param {jqXHR} xhr
 * @param {string} textStatus
 * @param {string} errorThrown
 * @param {string} context 操作上下文描述
 */
function handleAjaxError(xhr, textStatus, errorThrown, context) {
    var error = parseError(xhr, textStatus, errorThrown);
    if (context) {
        error.title = context + ' - ' + error.title;
    }
    showError(error);
    return error;
}

// ========== 数据校验 ==========

/**
 * 校验成绩输入
 * @param {number|string} value 输入值
 * @param {number} fullScore 满分
 * @param {boolean} isAbsent 是否缺考
 * @returns {object} { valid: boolean, message: string }
 */
function validateScore(value, fullScore, isAbsent) {
    if (isAbsent) return { valid: true, message: '' };

    var strVal = String(value).trim();
    if (strVal === '') {
        return { valid: false, message: '分数不能为空（如缺考请勾选"缺考"）' };
    }

    var numVal = parseFloat(strVal);
    if (isNaN(numVal)) {
        return { valid: false, message: '请输入有效数字' };
    }

    if (!isFinite(numVal)) {
        return { valid: false, message: '分数超出有效范围' };
    }

    if (numVal < 0) {
        return { valid: false, message: '分数不能为负数' };
    }

    if (numVal > fullScore) {
        return { valid: false, message: '分数 ' + numVal + ' 超出满分 ' + fullScore };
    }

    // 精度检查：最多1位小数
    var parts = strVal.split('.');
    if (parts.length > 1 && parts[1].length > 2) {
        return { valid: false, message: '分数最多保留两位小数' };
    }

    return { valid: true, message: '' };
}

/**
 * 批量校验：收集所有待保存的校验错误
 * @param {object} scoreChanged
 * @param {Array} allSubjects
 * @returns {Array} 错误列表
 */
function collectValidationErrors(scoreChanged, allSubjects) {
    var errors = [];
    var fullScoreMap = {};
    allSubjects.forEach(function (sub) {
        fullScoreMap[sub.subjectId] = sub.fullScore || 100;
    });

    Object.keys(scoreChanged).forEach(function (key) {
        var parts = key.split('_');
        var studentId = parts[0];
        var subjectId = parseInt(parts[1]);
        var item = scoreChanged[key];
        var absent = (typeof item === 'object') ? item.absent : false;
        var val = (typeof item === 'object') ? item.val : item;
        var fullScore = fullScoreMap[subjectId] || 100;

        var result = validateScore(val, fullScore, absent);
        if (!result.valid) {
            errors.push({
                studentId: studentId,
                subjectId: subjectId,
                message: result.message
            });
        }
    });

    return errors;
}

/**
 * 显示校验错误汇总
 * @param {Array} errors
 * @returns {boolean} 是否通过校验
 */
function showValidationSummary(errors) {
    if (errors.length === 0) return true;

    var msg = '数据校验发现 ' + errors.length + ' 个问题：\n\n';
    var maxShow = 10;
    errors.slice(0, maxShow).forEach(function (err, i) {
        msg += (i + 1) + '. 学生ID:' + err.studentId + ' 科目ID:' + err.subjectId + ' - ' + err.message + '\n';
    });
    if (errors.length > maxShow) {
        msg += '... 还有 ' + (errors.length - maxShow) + ' 个问题\n';
    }
    msg += '\n请修正以上问题后再保存。';

    alert(msg);
    return false;
}

// ========== 兼容旧版 htmlEncode ==========
// 保留别名，确保已有代码无需修改
var htmlEncode = safeHtml;