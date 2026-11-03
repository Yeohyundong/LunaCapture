CREATE TABLE `feedback` (
	`id` text PRIMARY KEY NOT NULL,
	`content` text NOT NULL,
	`reply_to` text,
	`version` text NOT NULL,
	`created` integer NOT NULL
);
--> statement-breakpoint
CREATE TABLE `feedback_limits` (
	`key` text PRIMARY KEY NOT NULL,
	`count` integer NOT NULL,
	`expires` integer NOT NULL
);
