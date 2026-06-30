-- 教职工角色字段改为支持多角色（逗号分隔）
ALTER TABLE Admin MODIFY COLUMN Role varchar(100) DEFAULT NULL COMMENT '角色（支持多角色，逗号分隔）';

-- 如果有已存在的单角色数据，无需修改，直接兼容
