import { sqliteTable, text, integer } from 'drizzle-orm/sqlite-core';
export const feedback = sqliteTable('feedback', {
 id: text('id').primaryKey(), content: text('content').notNull(), replyTo: text('reply_to'),
 version: text('version').notNull(), created: integer('created').notNull(),
});
export const limits = sqliteTable('feedback_limits', {
 key: text('key').primaryKey(), count: integer('count').notNull(), expires: integer('expires').notNull(),
});
